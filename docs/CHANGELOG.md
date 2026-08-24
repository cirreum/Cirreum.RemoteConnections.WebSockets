# Changelog

All notable changes to **Cirreum.RemoteConnections.WebSockets** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For detailed migration steps on major version bumps, see the per-version migration
guides linked at the bottom of each entry.

---

## [Unreleased]

Initial release of **Cirreum.RemoteConnections.WebSockets**.

### Added

- `WebSocketRemoteConnection` — abstract `ClientWebSocket`-backed `IRemoteConnection` with adapter-owned receive and reconnect loops.
- `WebSocketRemoteConnectionContext` — framework-constructed carrier holding a socket factory, so each connect and reconnect builds a fresh socket and re-resolves the access token.
- `OnFrameReceivedAsync` routing seam — defaults to the Cirreum `{ "method", "payload" }` envelope; override it to bridge third-party wire formats.
- Protected `SendBytesAsync` for binary frames, and a `SubProtocol` property reporting the server-negotiated subprotocol.
- Access-token posture resolution applied per connection attempt. Where a host cannot send headers on an upgrade, a bearer credential travels as an `access_token` query parameter instead; a non-bearer credential has no query equivalent and is rejected rather than silently dropped.
- `On<T>` registrations are held by the connection rather than the socket, so they may be made before the first connect and survive reconnects.
- Sends are serialized — a WebSocket permits one write at a time, so concurrent callers queue rather than corrupting the stream.
- `ConnectAsync` is idempotent and coalesces concurrent callers; `ConnectionId` is adapter-assigned and stable across reconnects.
- Reconnection can be disabled through `RemoteConnectionOptions.Reconnect`, in which case a loss ends at `Disconnected` and recovery is the application's.
