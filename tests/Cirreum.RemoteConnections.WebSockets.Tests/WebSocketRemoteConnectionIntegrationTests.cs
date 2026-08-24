namespace Cirreum.RemoteConnections.WebSockets.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

/// <summary>
/// Exercises a real <c>ClientWebSocket</c> against a WebSocket endpoint hosted in Kestrel on
/// loopback. <c>TestServer</c> cannot serve these: it does not support the upgrade.
/// </summary>
public sealed class WebSocketRemoteConnectionIntegrationTests : IAsyncLifetime {

	private WebApplication _app = null!;
	private Uri _endpoint = null!;

	private readonly ConcurrentBag<string?> _seenAuthorization = [];
	private readonly ConcurrentBag<string?> _seenAppName = [];

	/// <summary>
	/// A message the server answers by aborting the live socket. The drop has to act on the
	/// open connection: a flag consulted when accepting one cannot reach a socket already
	/// established, and would leak into whichever test connected next.
	/// </summary>
	private const string KillMessage = "kill";

	// Connection under test ——————————————————————————————————————

	public sealed record Echo(string Text);

	public sealed class EchoConnection(WebSocketRemoteConnectionContext context)
		: WebSocketRemoteConnection(context) {

		public int ReconnectedHookCalls;
		public readonly ConcurrentBag<byte[]> BinaryFrames = [];

		public IDisposable OnEcho(Func<Echo, Task> handler) => this.On("Echo", handler);

		public Task SayAsync(string text, CancellationToken ct = default) =>
			this.SendAsync("Say", new Echo(text), ct);

		public Task SendAudioAsync(byte[] bytes, CancellationToken ct = default) =>
			this.SendBytesAsync(bytes, WebSocketMessageType.Binary, ct);

		public string? Negotiated => this.SubProtocol;

		protected override Task OnReconnectedAsync(CancellationToken cancellationToken) {
			Interlocked.Increment(ref this.ReconnectedHookCalls);
			return Task.CompletedTask;
		}

		protected override async ValueTask OnFrameReceivedAsync(
			ReadOnlyMemory<byte> payload, WebSocketMessageType messageType, CancellationToken ct) {

			if (messageType == WebSocketMessageType.Binary) {
				this.BinaryFrames.Add(payload.ToArray());
				return;
			}

			await base.OnFrameReceivedAsync(payload, messageType, ct);
		}

	}

	public async Task InitializeAsync() {

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Logging.ClearProviders();

		this._app = builder.Build();
		this._app.UseWebSockets();

		this._app.Map("/media", async (HttpContext context) => {

			if (!context.WebSockets.IsWebSocketRequest) {
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				return;
			}

			this._seenAuthorization.Add(context.Request.Headers.Authorization.ToString());
			this._seenAppName.Add(context.Request.Headers["X-Cirreum-App-Name"].ToString());

			using var socket = await context.WebSockets.AcceptWebSocketAsync();
			await PumpAsync(socket, context.RequestAborted);
		});

		await this._app.StartAsync();

		var address = this._app.Services
			.GetRequiredService<IServer>()
			.Features.Get<IServerAddressesFeature>()!
			.Addresses.First();

		this._endpoint = new Uri(address.Replace("http://", "ws://") + "/media");
	}

	/// <summary>Echoes text back inside the envelope, and binary back verbatim.</summary>
	private static async Task PumpAsync(WebSocket socket, CancellationToken cancellationToken) {

		var buffer = new byte[8192];

		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested) {

			WebSocketReceiveResult result;
			using var message = new MemoryStream();

			try {
				do {
					result = await socket.ReceiveAsync(buffer, cancellationToken);
					if (result.MessageType == WebSocketMessageType.Close) {
						return;
					}
					message.Write(buffer, 0, result.Count);
				} while (!result.EndOfMessage);
			} catch (Exception) {
				return;
			}

			if (result.MessageType == WebSocketMessageType.Binary) {
				await socket.SendAsync(message.ToArray(), WebSocketMessageType.Binary, true, cancellationToken);
				continue;
			}

			// Unwrap { method, payload }; a kill message drops the live socket so the client
			// observes a genuine connection loss.
			using var document = JsonDocument.Parse(message.ToArray());
			var payload = document.RootElement.GetProperty("payload");

			if (payload.TryGetProperty("text", out var text) && text.GetString() == KillMessage) {
				socket.Abort();
				return;
			}

			var reply = JsonSerializer.SerializeToUtf8Bytes(new { method = "Echo", payload });

			await socket.SendAsync(reply, WebSocketMessageType.Text, true, cancellationToken);
		}
	}

	public async Task DisposeAsync() {
		await this._app.StopAsync();
		await this._app.DisposeAsync();
	}

	private EchoConnection CreateConnection(Action<RemoteConnectionOptions>? configure = null) {

		var options = new RemoteConnectionOptions("IntegrationApp") {
			EndpointUri = this._endpoint,
			AuthorizationHeader = AuthorizationHeaderSettings.None,
			ReconnectMaxDelay = TimeSpan.FromSeconds(1)
		};
		configure?.Invoke(options);

		return new EchoConnection(WebSocketRemoteConnectionContext.Create(
			new ServiceCollection().BuildServiceProvider(), options));
	}

	private static async Task<T> WaitAsync<T>(TaskCompletionSource<T> tcs) =>
		await tcs.Task.WaitAsync(TimeSpan.FromSeconds(20));

	// Round trip ————————————————————————————————————————————————

	[Fact]
	public async Task TheEnvelopeRoundTrips() {
		await using var connection = this.CreateConnection();
		var heard = new TaskCompletionSource<string>();

		// Registered before connect — this connection holds registrations, not the socket.
		using var subscription = connection.OnEcho(e => { heard.TrySetResult(e.Text); return Task.CompletedTask; });

		await connection.ConnectAsync();
		await connection.SayAsync("hello");

		(await WaitAsync(heard)).Should().Be("hello");
	}

	[Fact]
	public async Task BinaryFramesReachTheOverriddenSeam() {
		await using var connection = this.CreateConnection();
		await connection.ConnectAsync();

		await connection.SendAudioAsync([1, 2, 3, 4]);

		var deadline = DateTime.UtcNow.AddSeconds(20);
		while (connection.BinaryFrames.IsEmpty && DateTime.UtcNow < deadline) {
			await Task.Delay(25);
		}

		connection.BinaryFrames.Should().ContainSingle().Which.Should().Equal([1, 2, 3, 4]);
	}

	[Fact]
	public async Task AnUnsubscribedHandler_StopsReceiving() {
		await using var connection = this.CreateConnection();
		var received = 0;
		var subscription = connection.OnEcho(_ => { Interlocked.Increment(ref received); return Task.CompletedTask; });

		await connection.ConnectAsync();
		subscription.Dispose();
		await connection.SayAsync("ignored");
		await Task.Delay(500);

		received.Should().Be(0);
	}

	// Credentials ———————————————————————————————————————————————

	[Fact]
	public async Task TheCredentialAndApplicationNameReachTheServer() {
		await using var connection = this.CreateConnection(o =>
			o.AuthorizationHeader = new AuthorizationHeaderSettings { Scheme = "Bearer", Value = "st_prod_abc123" });

		await connection.ConnectAsync();

		this._seenAuthorization.Should().Contain("Bearer st_prod_abc123",
			"a prefix is part of the opaque secret and travels verbatim");
		this._seenAppName.Should().Contain("IntegrationApp");
	}

	[Fact]
	public async Task MissingCredentials_FailConnectRatherThanConnectingAnonymously() {
		await using var connection = this.CreateConnection(o => o.AuthorizationHeader = null);

		var act = async () => await connection.ConnectAsync();

		await act.Should().ThrowAsync<InvalidOperationException>();
		connection.State.Should().Be(RemoteConnectionState.Disconnected);
	}

	// Lifecycle —————————————————————————————————————————————————

	[Fact]
	public async Task Connect_TransitionsThroughConnectingToConnected() {
		await using var connection = this.CreateConnection();
		List<RemoteConnectionState> observed = [];
		connection.StateChanged += (_, e) => { lock (observed) { observed.Add(e.NewState); } };

		await connection.ConnectAsync();

		connection.State.Should().Be(RemoteConnectionState.Connected);
		observed.Should().ContainInOrder(RemoteConnectionState.Connecting, RemoteConnectionState.Connected);
	}

	[Fact]
	public async Task ConnectAsync_IsIdempotentAndCoalesces() {
		await using var connection = this.CreateConnection();

		await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => connection.ConnectAsync()));

		connection.State.Should().Be(RemoteConnectionState.Connected);
	}

	[Fact]
	public async Task Disconnect_ReturnsToDisconnected() {
		await using var connection = this.CreateConnection();
		await connection.ConnectAsync();

		await connection.DisconnectAsync();

		connection.State.Should().Be(RemoteConnectionState.Disconnected);
	}

	[Fact]
	public async Task DisconnectWhenNeverConnected_IsSafe() {
		await using var connection = this.CreateConnection();

		await connection.DisconnectAsync();

		connection.State.Should().Be(RemoteConnectionState.Disconnected);
	}

	// Reconnect —————————————————————————————————————————————————

	[Fact]
	public async Task AServerDrop_ReconnectsAndInvokesTheHook() {
		await using var connection = this.CreateConnection();
		var reconnected = new TaskCompletionSource<bool>();
		connection.StateChanged += (_, e) => {
			if (e.NewState == RemoteConnectionState.Connected && e.PreviousState == RemoteConnectionState.Reconnecting) {
				reconnected.TrySetResult(true);
			}
		};

		await connection.ConnectAsync();

		// The server aborts the live socket, which the client sees as a loss.
		await connection.SayAsync(KillMessage);

		(await WaitAsync(reconnected)).Should().BeTrue();
		connection.State.Should().Be(RemoteConnectionState.Connected);
		connection.ReconnectedHookCalls.Should().BeGreaterThan(0,
			"state that does not survive a reconnect is restored here");
	}

	[Fact]
	public async Task HandlersAndConnectionIdSurviveAReconnect() {
		await using var connection = this.CreateConnection();
		var id = connection.ConnectionId;
		var afterReconnect = new TaskCompletionSource<string>();
		var reconnected = new TaskCompletionSource<bool>();

		using var subscription = connection.OnEcho(e => {
			if (e.Text == "after") { afterReconnect.TrySetResult(e.Text); }
			return Task.CompletedTask;
		});
		connection.StateChanged += (_, e) => {
			if (e.NewState == RemoteConnectionState.Connected && e.PreviousState == RemoteConnectionState.Reconnecting) {
				reconnected.TrySetResult(true);
			}
		};

		await connection.ConnectAsync();
		await connection.SayAsync(KillMessage);
		await WaitAsync(reconnected);

		await connection.SayAsync("after");

		(await WaitAsync(afterReconnect)).Should().Be("after");
		connection.ConnectionId.Should().Be(id, "the contract promises an identifier stable across reconnects");
	}

	[Fact]
	public async Task WithReconnectDisabled_ALossEndsAtDisconnected() {
		await using var connection = this.CreateConnection(o => o.Reconnect = false);
		var disconnected = new TaskCompletionSource<bool>();
		connection.StateChanged += (_, e) => {
			if (e.NewState == RemoteConnectionState.Disconnected) { disconnected.TrySetResult(true); }
		};

		await connection.ConnectAsync();
		await connection.SayAsync(KillMessage);

		(await WaitAsync(disconnected)).Should().BeTrue();
		connection.State.Should().Be(RemoteConnectionState.Disconnected);
		connection.ReconnectedHookCalls.Should().Be(0);
	}

	// Disposal ——————————————————————————————————————————————————

	[Fact]
	public async Task AfterDisposal_TheConnectionRefusesUse() {
		var connection = this.CreateConnection();
		await connection.ConnectAsync();

		await connection.DisposeAsync();

		connection.State.Should().Be(RemoteConnectionState.Disconnected);
		await ((Func<Task>)(() => connection.ConnectAsync())).Should().ThrowAsync<ObjectDisposedException>();
		await ((Func<Task>)(() => connection.SayAsync("x"))).Should().ThrowAsync<ObjectDisposedException>();
	}

	[Fact]
	public async Task Disposal_IsIdempotentAndSafeBeforeConnecting() {
		var connected = this.CreateConnection();
		await connected.ConnectAsync();
		await connected.DisposeAsync();
		await connected.DisposeAsync();

		var never = this.CreateConnection();
		await never.DisposeAsync();
	}

}
