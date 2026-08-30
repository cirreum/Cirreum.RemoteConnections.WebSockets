namespace Cirreum.RemoteServices.Connections;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;

/// <summary>
/// Builds a configured <see cref="ClientWebSocket"/> and the URI to connect it to.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ClientWebSocket"/> is single-use: it cannot be reconnected once closed. Every
/// connect and reconnect attempt therefore builds a fresh one, which is what makes the
/// credential re-resolve per attempt — a token refreshes across reconnects with no further
/// machinery.
/// </para>
/// </remarks>
internal sealed class WebSocketConnectionFactory(
	RemoteConnectionOptions options,
	Type connectionType,
	IServiceProvider services,
	ILogger logger,
	string connectionId,
	Action<ClientWebSocketOptions>? configureTransport) {

	private const string BearerScheme = "Bearer";
	private const string AccessTokenQueryParameter = "access_token";

	internal async ValueTask<(ClientWebSocket Socket, Uri Uri)> CreateAsync(CancellationToken cancellationToken) {

		var socket = new ClientWebSocket();
		var uri = options.EndpointUri;

		if (options.ApplicationName.HasValue() && !OperatingSystem.IsBrowser()) {
			socket.Options.SetRequestHeader(RemoteIdentityConstants.AppNameHeader, options.ApplicationName);
		}

		var credential = await this.ResolveCredentialAsync(cancellationToken).ConfigureAwait(false);
		if (credential is { HasValue: true }) {
			uri = Apply(socket, uri, credential.Scheme, credential.Value);
		}

		// The application's delegate runs last, so a setting it makes wins over the framework's.
		configureTransport?.Invoke(socket.Options);

		return (socket, uri);
	}

	/// <summary>
	/// Attach the credential where this host can carry it. A browser cannot set headers on a
	/// WebSocket upgrade, so the credential travels in the query instead — the convention
	/// SignalR's own browser clients follow, and what the server spine reads on a connection
	/// endpoint.
	/// </summary>
	private static Uri Apply(ClientWebSocket socket, Uri uri, string scheme, string value) {

		if (!OperatingSystem.IsBrowser()) {
			socket.Options.SetRequestHeader("Authorization", $"{scheme} {value}");
			return uri;
		}

		if (!string.Equals(scheme, BearerScheme, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(
				$"A browser cannot send an Authorization header on a WebSocket upgrade, and only a " +
				$"{BearerScheme} credential has a query-parameter equivalent. The connection to " +
				$"'{uri}' is configured with the '{scheme}' scheme.");
		}

		var builder = new UriBuilder(uri);
		var separator = string.IsNullOrEmpty(builder.Query) ? string.Empty : "&";
		builder.Query = $"{builder.Query.TrimStart('?')}{separator}" +
			$"{AccessTokenQueryParameter}={Uri.EscapeDataString(value)}";

		return builder.Uri;
	}

	/// <summary>
	/// Resolve the credential for this attempt. Postures, in precedence order: an explicit
	/// callback, an explicit header, an explicit choice to connect without credentials, then
	/// the ambient <see cref="IRemoteConnectionCredentialSource"/>, preferring one registered
	/// against the connection's type.
	/// </summary>
	/// <returns>
	/// The credential to attach, or <see langword="null"/> to attach none. A credential that is
	/// wanted but unavailable faults the attempt rather than returning here.
	/// </returns>
	private async ValueTask<AuthorizationHeaderSettings?> ResolveCredentialAsync(CancellationToken cancellationToken) {

		if (options.CredentialProvider is { } callback) {
			this.LogPosture("explicit callback");
			return this.Require(await callback(cancellationToken).ConfigureAwait(false));
		}

		var header = options.AuthorizationHeader;

		if (header is { HasValue: true }) {
			this.LogPosture($"static {header.Scheme} header");
			return header;
		}

		if (header is not null) {
			this.LogPosture("explicitly public");
			return null;
		}

		this.LogPosture("ambient credential source");

		var source = this.ResolveSource()
			?? throw new InvalidOperationException(
				$"No credentials are available for the remote connection to '{options.EndpointUri}'. " +
				$"Supply a credential callback or an authorization header on its options, register " +
				$"an {nameof(IRemoteConnectionCredentialSource)}, or set the authorization header to " +
				$"{nameof(AuthorizationHeaderSettings)}.{nameof(AuthorizationHeaderSettings.None)} " +
				$"to connect without credentials.");

		var request = new RemoteConnectionCredentialRequest {
			EndpointUri = options.EndpointUri,
			Scopes = options.Scopes,
			ConnectionType = connectionType,
		};

		return this.Require(await source.GetCredentialAsync(request, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>
	/// Prefers a source registered against the connection's own type, so one connection can use a
	/// different credential mechanism than another, and falls back to the unkeyed registration.
	/// </summary>
	private IRemoteConnectionCredentialSource? ResolveSource() {

		if (services is IKeyedServiceProvider) {
			var keyed = services.GetKeyedService<IRemoteConnectionCredentialSource>(connectionType);
			if (keyed is not null) {
				return keyed;
			}
		}

		return services.GetService<IRemoteConnectionCredentialSource>();

	}

	/// <summary>
	/// Distinguishes a deliberate decision to present nothing from an absent credential. The
	/// second faults the attempt: connecting anyway would send an unauthenticated upgrade that
	/// the server refuses, which reads as an application fault rather than a missing credential.
	/// </summary>
	private AuthorizationHeaderSettings? Require(AuthorizationHeaderSettings? resolved) {

		return resolved ?? throw new InvalidOperationException(
			$"No credential was supplied for the remote connection to '{options.EndpointUri}'. " +
			$"Declare the scopes it should be requested for on the connection's options, or set the " +
			$"authorization header to {nameof(AuthorizationHeaderSettings)}." +
			$"{nameof(AuthorizationHeaderSettings.None)} to connect without credentials.");

	}

	private void LogPosture(string posture) => logger.LogCredentialPosture(connectionId, posture);

}
