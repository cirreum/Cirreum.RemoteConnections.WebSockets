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
- Access-token posture resolution applied per connection attempt.
