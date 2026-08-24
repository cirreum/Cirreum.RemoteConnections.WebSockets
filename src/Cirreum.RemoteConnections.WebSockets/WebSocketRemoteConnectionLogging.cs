namespace Cirreum.RemoteServices;

using Microsoft.Extensions.Logging;

/// <summary>
/// Source-generated logging methods for raw WebSocket remote connections.
/// </summary>
internal static partial class WebSocketRemoteConnectionLogging {

	[LoggerMessage(
		EventId = 3001,
		Level = LogLevel.Information,
		Message = "Remote connection {ConnectionId} connected to {Endpoint} with subprotocol {SubProtocol}")]
	internal static partial void LogConnected(
		this ILogger logger,
		string connectionId,
		string endpoint,
		string? subProtocol);

	[LoggerMessage(
		EventId = 3002,
		Level = LogLevel.Error,
		Message = "Remote connection {ConnectionId} failed to connect to {Endpoint}")]
	internal static partial void LogConnectFailed(
		this ILogger logger,
		Exception exception,
		string connectionId,
		string endpoint);

	[LoggerMessage(
		EventId = 3003,
		Level = LogLevel.Warning,
		Message = "Remote connection {ConnectionId} lost; reconnecting")]
	internal static partial void LogReconnecting(
		this ILogger logger,
		Exception? exception,
		string connectionId);

	[LoggerMessage(
		EventId = 3004,
		Level = LogLevel.Warning,
		Message = "Remote connection {ConnectionId} reconnect attempt {Attempt} failed; retrying in {DelayMs}ms")]
	internal static partial void LogReconnectAttemptFailed(
		this ILogger logger,
		Exception exception,
		string connectionId,
		int attempt,
		double delayMs);

	[LoggerMessage(
		EventId = 3005,
		Level = LogLevel.Information,
		Message = "Remote connection {ConnectionId} re-established after {Attempt} attempt(s)")]
	internal static partial void LogReconnected(
		this ILogger logger,
		string connectionId,
		int attempt);

	[LoggerMessage(
		EventId = 3006,
		Level = LogLevel.Information,
		Message = "Remote connection {ConnectionId} closed")]
	internal static partial void LogClosed(
		this ILogger logger,
		Exception? exception,
		string connectionId);

	[LoggerMessage(
		EventId = 3007,
		Level = LogLevel.Debug,
		Message = "Remote connection {ConnectionId} resolved credentials using the {Posture} posture")]
	internal static partial void LogTokenPosture(
		this ILogger logger,
		string connectionId,
		string posture);

	[LoggerMessage(
		EventId = 3008,
		Level = LogLevel.Warning,
		Message = "Remote connection {ConnectionId} could not route an inbound frame")]
	internal static partial void LogFrameRoutingFailed(
		this ILogger logger,
		Exception exception,
		string connectionId);

	[LoggerMessage(
		EventId = 3009,
		Level = LogLevel.Warning,
		Message = "Remote connection {ConnectionId} handler for '{Method}' threw")]
	internal static partial void LogHandlerFailed(
		this ILogger logger,
		Exception exception,
		string connectionId,
		string method);

}
