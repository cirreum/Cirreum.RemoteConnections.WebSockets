# Changelog

All notable changes to **Cirreum.RemoteConnections.WebSockets** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For detailed migration steps on major version bumps, see the per-version migration
guides linked at the bottom of each entry.

---

## [Unreleased]

### Updated

- Updated NuGet packages.

## [2.0.0] - 2026-08-25

### Breaking

* **The connection types move to `Cirreum.RemoteServices.Connections`**, following
  `Cirreum.Contracts` 5.0.0 and `Cirreum.Domain` 5.0.0. A service is something you call; a
  connection is something you hold open, so it nests rather than sitting alongside.

* **`WebSocketRemoteConnectionContext.Create` is generic**:
  `Create<TConnection>(services, options, configureTransport)`. A credential source may be
  registered against the connection's type, and this package is where the source is resolved, so
  the type has to reach it.

* **The credential seam follows `Cirreum.Contracts` 5.0.0.** The ambient source is
  `IRemoteConnectionCredentialSource`, resolved with a `RemoteConnectionTokenRequest` and returning
  `AuthorizationHeaderSettings?`; the per-connection callback is
  `RemoteConnectionOptions.CredentialProvider`.

  A resolved credential now has three answers. A value is presented.
  `AuthorizationHeaderSettings.None` connects deliberately without one. `null` means none is
  available and **fails the attempt** — a change from 1.0, where a callback or source returning
  nothing produced an unauthenticated upgrade that the server refused later.

  A callback also now supplies a full credential rather than a bare token, so it is no longer
  assumed to be Bearer.

### Added

* **A credential source may be registered keyed to a connection type**, and is preferred over the
  unkeyed registration for that connection — so one connection can use a different mechanism or
  identity provider than another.

* **`RemoteConnectionOptions.Scopes` reaches the source**, which is what lets a host mint a
  credential for the audience the application named rather than for its own defaults.

* **Any scheme may be resolved per attempt**, not only Bearer. A fresh socket is built per attempt
  and its headers are set after the credential resolves, so an ApiKey or other scheme refreshes
  across reconnects like a token does. The browser path is unchanged: it carries the credential as
  an `access_token` query parameter, which only Bearer has an equivalent for.

### Updated

- `Cirreum.Domain` 5.0.0.

### Updated

- Updated NuGet packages.

## [1.0.1] - 2026-08-25

### Fixed

* **The README described direct composition as the only way to register a connection.** Cirreum
  applications composing through an application builder register through the matching Runtime
  Extensions package, which is the path most consumers of this package take. The README now says
  so, while keeping direct composition documented for hosts that compose services themselves.

## [1.0.0] - 2026-08-24

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
