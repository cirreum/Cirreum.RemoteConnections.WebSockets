namespace Cirreum.RemoteServices.Connections;

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

/// <summary>
/// Base class for raw WebSocket client connections. Owns the connection's lifetime, state
/// machine, receive and reconnect loops, and disposal; derived types expose the endpoint's
/// messages as typed members.
/// </summary>
/// <remarks>
/// <para>
/// A derived type declares <see cref="WebSocketRemoteConnectionContext"/> as its first
/// constructor parameter and passes it to the base constructor.
/// </para>
/// <para>
/// Inbound frames arrive at <see cref="OnFrameReceivedAsync"/>. The default implementation
/// decodes Cirreum's <c>{ "method", "payload" }</c> envelope and dispatches to handlers
/// registered through <see cref="IRemoteConnection.On{T}"/>, which is what a Cirreum server
/// writes for a method-addressed push. Bridging a protocol owned by someone else — telephony
/// media, a realtime speech API — means overriding that method and owning the discriminator.
/// </para>
/// <para>
/// Request/response has no counterpart here: the transport provides none, and synthesizing one
/// would mean rebuilding correlation, timeouts and cancellation over a one-way pipe.
/// </para>
/// </remarks>
/// <remarks>Initializes a new instance from the framework-supplied context.</remarks>
/// <param name="context">The connection's socket factory, options and logger.</param>
public abstract class WebSocketRemoteConnection(
	WebSocketRemoteConnectionContext context
) : RemoteConnectionBase(context.Logger)
  , IAsyncDisposable {

	private readonly SemaphoreSlim _connectGate = new(1, 1);
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<JsonElement, Task>>> _handlers =
		new(StringComparer.Ordinal);
	private readonly ReconnectDelaySchedule _delays = new ReconnectDelaySchedule(context.Options.ReconnectMaxDelay);

	private ClientWebSocket? _socket;
	private CancellationTokenSource? _loopCancellation;
	private Task? _receiveLoop;
	private bool _disposed;

	/// <inheritdoc/>
	/// <remarks>
	/// Assigned by the adapter and stable for the life of this instance, including across
	/// reconnects. The transport has no identifier of its own to expose.
	/// </remarks>
	public override string ConnectionId => context.ConnectionId;

	/// <summary>
	/// The subprotocol the server selected, or <see langword="null"/> when none was negotiated
	/// or the connection is not open.
	/// </summary>
	/// <remarks>
	/// Re-read from the live socket, so it reflects the current connection: each reconnect
	/// negotiates afresh, and a derived type that adapts its framing to the negotiated protocol
	/// must read this rather than cache it. Offer protocols through the transport configure
	/// delegate.
	/// </remarks>
	public string? SubProtocol => this._socket?.SubProtocol;

	/// <summary>The serializer used for the default envelope. Override to supply a source-generated resolver.</summary>
	protected virtual JsonSerializerOptions SerializerOptions { get; } =
		new(JsonSerializerDefaults.Web);

	/// <inheritdoc/>
	public override async Task ConnectAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(this._disposed, this);

		await this._connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			ObjectDisposedException.ThrowIf(this._disposed, this);

			if (this._socket?.State == WebSocketState.Open) {
				return;
			}

			this.TransitionTo(RemoteConnectionState.Connecting);

			try {
				await this.OpenAsync(cancellationToken).ConfigureAwait(false);
				await this.OnConnectedAsync(cancellationToken).ConfigureAwait(false);
			} catch (Exception ex) {
				this.TransitionTo(RemoteConnectionState.Disconnected);
				this.Logger.LogConnectFailed(ex, this.ConnectionId, context.Options.EndpointUri.ToString());
				throw;
			}

			this.TransitionTo(RemoteConnectionState.Connected);
			var connectedEndpoint = context.Options.EndpointUri.ToString();
			this.Logger.LogConnected(
				this.ConnectionId, connectedEndpoint, this.SubProtocol);

			this.StartReceiveLoop();
		} finally {
			this._connectGate.Release();
		}
	}

	/// <inheritdoc/>
	public override async Task DisconnectAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(this._disposed, this);

		await this._connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			if (this._socket is null) {
				this.TransitionTo(RemoteConnectionState.Disconnected);
				return;
			}

			this.TransitionTo(RemoteConnectionState.Disconnecting);
			await this.StopLoopAsync().ConfigureAwait(false);
			await this.CloseSocketAsync(cancellationToken).ConfigureAwait(false);
			this.TransitionTo(RemoteConnectionState.Disconnected);
		} finally {
			this._connectGate.Release();
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Valid in any state, and registrations survive a reconnect: this connection holds them,
	/// not the socket. Handlers are dispatched by the default frame routing; a derived type
	/// that overrides <see cref="OnFrameReceivedAsync"/> decides whether they apply at all.
	/// </remarks>
	public override IDisposable On<T>(string method, Func<T, Task> handler) {
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ArgumentNullException.ThrowIfNull(handler);

		var id = Guid.NewGuid();
		var forMethod = this._handlers.GetOrAdd(method, _ => new ConcurrentDictionary<Guid, Func<JsonElement, Task>>());

		forMethod[id] = payload => {
			var typed = payload.Deserialize<T>(this.SerializerOptions);
			return typed is null ? Task.CompletedTask : handler(typed);
		};

		return new HandlerRegistration(() => forMethod.TryRemove(id, out _));
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Sends the Cirreum <c>{ "method", "payload" }</c> envelope as a text frame. A derived
	/// type bridging another protocol sends through <see cref="SendBytesAsync"/> instead.
	/// </remarks>
	public override async Task SendAsync<T>(string method, T payload, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ObjectDisposedException.ThrowIf(this._disposed, this);

		var envelope = JsonSerializer.SerializeToUtf8Bytes(
			new { method, payload }, this.SerializerOptions);

		await this.SendBytesAsync(envelope, WebSocketMessageType.Text, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Send a raw frame, for binary payloads and for protocols this connection does not encode.
	/// </summary>
	/// <param name="payload">The frame's bytes.</param>
	/// <param name="messageType">The frame type. Defaults to <see cref="WebSocketMessageType.Binary"/>.</param>
	/// <param name="cancellationToken">Cancellation token for the send.</param>
	/// <remarks>
	/// Sends are serialized: a WebSocket permits one write at a time, so concurrent callers
	/// queue rather than corrupting the stream.
	/// </remarks>
	protected async Task SendBytesAsync(
		ReadOnlyMemory<byte> payload,
		WebSocketMessageType messageType = WebSocketMessageType.Binary,
		CancellationToken cancellationToken = default) {

		ObjectDisposedException.ThrowIf(this._disposed, this);

		var socket = this._socket
			?? throw new InvalidOperationException(
				$"The remote connection to '{context.Options.EndpointUri}' is not connected.");

		await this._sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			await socket.SendAsync(payload, messageType, endOfMessage: true, cancellationToken)
				.ConfigureAwait(false);
		} finally {
			this._sendGate.Release();
		}
	}

	/// <summary>
	/// Called for each inbound message, after all of its frames have been assembled.
	/// </summary>
	/// <param name="payload">The complete message.</param>
	/// <param name="messageType">Whether the peer sent text or binary.</param>
	/// <param name="cancellationToken">Cancellation token for the connection's receive loop.</param>
	/// <remarks>
	/// The default implementation decodes Cirreum's <c>{ "method", "payload" }</c> envelope and
	/// dispatches to the handlers registered for that method. Override to read a protocol this
	/// connection does not own, in which case the envelope and the handler registry play no part.
	/// </remarks>
	protected virtual async ValueTask OnFrameReceivedAsync(
		ReadOnlyMemory<byte> payload,
		WebSocketMessageType messageType,
		CancellationToken cancellationToken) {

		if (messageType != WebSocketMessageType.Text) {
			return;
		}

		RemoteConnectionEnvelope? envelope;
		try {
			envelope = JsonSerializer.Deserialize<RemoteConnectionEnvelope>(payload.Span, this.SerializerOptions);
		} catch (JsonException ex) {
			this.Logger.LogFrameRoutingFailed(ex, this.ConnectionId);
			return;
		}

		if (envelope?.Method is not { Length: > 0 } method
			|| !this._handlers.TryGetValue(method, out var forMethod)) {
			return;
		}

		foreach (var handler in forMethod.Values) {
			try {
				await handler(envelope.Payload).ConfigureAwait(false);
			} catch (Exception ex) {
				// One handler's failure must not stop the receive loop or the others.
				this.Logger.LogHandlerFailed(ex, this.ConnectionId, method);
			}
		}
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync() {
		if (this._disposed) {
			return;
		}

		this._disposed = true;

		this.TransitionTo(RemoteConnectionState.Disconnecting);

		await this.StopLoopAsync().ConfigureAwait(false);
		await this.CloseSocketAsync(CancellationToken.None).ConfigureAwait(false);

		this.TransitionTo(RemoteConnectionState.Disconnected);

		this._connectGate.Dispose();
		this._sendGate.Dispose();

		GC.SuppressFinalize(this);
	}

	private async Task OpenAsync(CancellationToken cancellationToken) {
		var (socket, uri) = await context.SocketFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		try {
			await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
		} catch {
			socket.Dispose();
			throw;
		}

		this._socket = socket;
	}

	private void StartReceiveLoop() {
		this._loopCancellation = new CancellationTokenSource();
		this._receiveLoop = Task.Run(() => this.ReceiveLoopAsync(this._loopCancellation.Token));
	}

	private async Task ReceiveLoopAsync(CancellationToken cancellationToken) {

		var buffer = new byte[8192];

		while (!cancellationToken.IsCancellationRequested) {

			var socket = this._socket;
			if (socket is null || socket.State != WebSocketState.Open) {
				break;
			}

			using var message = new MemoryStream();
			ValueWebSocketReceiveResult result;

			try {
				do {
					result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

					if (result.MessageType == WebSocketMessageType.Close) {
						await this.HandleConnectionLostAsync(null, cancellationToken).ConfigureAwait(false);
						return;
					}

					message.Write(buffer, 0, result.Count);
				} while (!result.EndOfMessage);
			} catch (OperationCanceledException) {
				return;
			} catch (Exception ex) {
				await this.HandleConnectionLostAsync(ex, cancellationToken).ConfigureAwait(false);
				return;
			}

			try {
				await this.OnFrameReceivedAsync(message.ToArray(), result.MessageType, cancellationToken)
					.ConfigureAwait(false);
			} catch (Exception ex) {
				// Routing is the application's code once overridden; a fault there must not
				// take the connection down.
				this.Logger.LogFrameRoutingFailed(ex, this.ConnectionId);
			}
		}
	}

	private async Task HandleConnectionLostAsync(Exception? exception, CancellationToken cancellationToken) {

		if (this._disposed || cancellationToken.IsCancellationRequested) {
			return;
		}

		this.Logger.LogClosed(exception, this.ConnectionId);
		DisposeSocket(ref this._socket);

		if (!context.Options.Reconnect) {
			this.TransitionTo(RemoteConnectionState.Disconnected);
			return;
		}

		this.Logger.LogReconnecting(exception, this.ConnectionId);
		this.TransitionTo(RemoteConnectionState.Reconnecting);

		await this.ReconnectAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task ReconnectAsync(CancellationToken cancellationToken) {

		for (var attempt = 0; !cancellationToken.IsCancellationRequested && !this._disposed; attempt++) {

			var delay = this._delays.NextDelay(attempt);
			if (delay > TimeSpan.Zero) {
				try {
					await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
				} catch (OperationCanceledException) {
					return;
				}
			}

			try {
				await this.OpenAsync(cancellationToken).ConfigureAwait(false);
				await this.OnReconnectedAsync(cancellationToken).ConfigureAwait(false);

				this.TransitionTo(RemoteConnectionState.Connected);
				this.Logger.LogReconnected(this.ConnectionId, attempt + 1);

				// Continue receiving on the loop this method is running inside.
				await this.ReceiveLoopAsync(cancellationToken).ConfigureAwait(false);
				return;
			} catch (OperationCanceledException) {
				return;
			} catch (Exception ex) {
				this.Logger.LogReconnectAttemptFailed(
					ex, this.ConnectionId, attempt + 1, this._delays.NextDelay(attempt + 1).TotalMilliseconds);
			}
		}
	}

	private async Task StopLoopAsync() {

		if (this._loopCancellation is { } cancellation) {
			await cancellation.CancelAsync().ConfigureAwait(false);
		}

		if (this._receiveLoop is { } loop) {
			try {
				await loop.ConfigureAwait(false);
			} catch (OperationCanceledException) {
				// Expected: the loop observes cancellation.
			}
		}

		this._loopCancellation?.Dispose();
		this._loopCancellation = null;
		this._receiveLoop = null;
	}

	private async Task CloseSocketAsync(CancellationToken cancellationToken) {

		var socket = this._socket;
		if (socket is null) {
			return;
		}

		if (socket.State == WebSocketState.Open) {
			try {
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, cancellationToken)
					.ConfigureAwait(false);
			} catch (Exception ex) {
				// Closing is best effort; disposal must complete regardless.
				this.Logger.LogClosed(ex, this.ConnectionId);
			}
		}

		DisposeSocket(ref this._socket);
	}

	private static void DisposeSocket(ref ClientWebSocket? socket) {
		socket?.Dispose();
		socket = null;
	}

	private sealed class HandlerRegistration(Action unsubscribe) : IDisposable {

		private Action? _unsubscribe = unsubscribe;

		public void Dispose() {
			Interlocked.Exchange(ref this._unsubscribe, null)?.Invoke();
		}

	}

}
