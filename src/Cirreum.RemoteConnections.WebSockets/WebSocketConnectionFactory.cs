namespace Cirreum.RemoteServices;

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
		if (credential is { Scheme.Length: > 0, Value.Length: > 0 }) {
			uri = Apply(socket, uri, credential.Value.Scheme, credential.Value.Value);
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
	/// the ambient <see cref="IRemoteConnectionTokenSource"/>.
	/// </summary>
	private async ValueTask<(string Scheme, string Value)?> ResolveCredentialAsync(CancellationToken cancellationToken) {

		if (options.AccessTokenProvider is { } callback) {
			this.LogPosture("explicit callback");
			var token = await callback(cancellationToken).ConfigureAwait(false);
			return token.HasValue() ? (BearerScheme, token) : null;
		}

		var header = options.AuthorizationHeader;

		if (header is { HasValue: true }) {
			this.LogPosture($"static {header.Scheme} header");
			return (header.Scheme, header.Value);
		}

		if (header is not null) {
			this.LogPosture("explicitly public");
			return null;
		}

		this.LogPosture("ambient token source");

		var source = services.GetService<IRemoteConnectionTokenSource>()
			?? throw new InvalidOperationException(
				$"No credentials are available for the remote connection to '{options.EndpointUri}'. " +
				$"Supply an access-token callback or an authorization header on its options, register " +
				$"an {nameof(IRemoteConnectionTokenSource)}, or set the authorization header to " +
				$"{nameof(AuthorizationHeaderSettings)}.{nameof(AuthorizationHeaderSettings.None)} " +
				$"to connect without credentials.");

		var ambient = await source.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
		return ambient.HasValue() ? (BearerScheme, ambient) : null;
	}

	private void LogPosture(string posture) => logger.LogTokenPosture(connectionId, posture);

}
