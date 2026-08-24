namespace Cirreum.RemoteServices;

using Microsoft.Extensions.Logging;
using System.Net.WebSockets;

/// <summary>
/// The framework-supplied dependencies of a <see cref="WebSocketRemoteConnection"/>.
/// </summary>
/// <remarks>
/// Derived connection types accept this as their first constructor parameter and pass it to
/// the base constructor. Additional application dependencies are declared alongside it and
/// resolve from the container as usual.
/// </remarks>
public sealed class WebSocketRemoteConnectionContext {

	internal WebSocketRemoteConnectionContext(
		WebSocketConnectionFactory socketFactory,
		RemoteConnectionOptions options,
		ILogger logger,
		string connectionId) {

		this.SocketFactory = socketFactory;
		this.Options = options;
		this.Logger = logger;
		this.ConnectionId = connectionId;
	}

	internal WebSocketConnectionFactory SocketFactory { get; }

	/// <summary>The options the connection was registered with.</summary>
	public RemoteConnectionOptions Options { get; }

	/// <summary>The logger for the connection.</summary>
	public ILogger Logger { get; }

	/// <summary>The adapter-assigned identifier, stable for the life of the connection.</summary>
	public string ConnectionId { get; }

	/// <summary>
	/// Build a context for a connection to the endpoint described by <paramref name="options"/>.
	/// </summary>
	/// <param name="services">The provider used to resolve the logger and, where the options
	/// do not specify credentials, the ambient <see cref="IRemoteConnectionTokenSource"/>.</param>
	/// <param name="options">The connection's options. The endpoint must be an absolute Uri.</param>
	/// <param name="configureTransport">
	/// An optional delegate applied to each socket's <see cref="ClientWebSocketOptions"/> after
	/// the framework has configured it, so that any transport setting may be overridden. Called
	/// once per connect and reconnect attempt, because each attempt builds a fresh socket.
	/// </param>
	public static WebSocketRemoteConnectionContext Create(
		IServiceProvider services,
		RemoteConnectionOptions options,
		Action<ClientWebSocketOptions>? configureTransport = null) {

		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(options);

		if (!options.EndpointUri.OriginalString.HasValue()) {
			throw new InvalidOperationException(
				$"A remote connection requires an {nameof(RemoteConnectionOptions.EndpointUri)}.");
		}

		if (!options.EndpointUri.IsAbsoluteUri) {
			throw new InvalidOperationException(
				$"{nameof(RemoteConnectionOptions.EndpointUri)} must be an absolute Uri. " +
				$"Unsupported: {options.EndpointUri}");
		}

		if (options.ReconnectMaxDelay <= TimeSpan.Zero) {
			throw new InvalidOperationException(
				$"{nameof(RemoteConnectionOptions.ReconnectMaxDelay)} must be greater than zero.");
		}

		var loggerFactory = services.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
		var logger = loggerFactory?.CreateLogger("Cirreum.RemoteServices.WebSocketRemoteConnection")
			?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

		var connectionId = Guid.NewGuid().ToString("N");

		var factory = new WebSocketConnectionFactory(
			options, services, logger, connectionId, configureTransport);

		return new WebSocketRemoteConnectionContext(factory, options, logger, connectionId);
	}

}
