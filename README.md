# Cirreum.RemoteConnections.WebSockets

[![NuGet Version](https://img.shields.io/nuget/v/Cirreum.RemoteConnections.WebSockets.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.RemoteConnections.WebSockets/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Cirreum.RemoteConnections.WebSockets.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.RemoteConnections.WebSockets/)
[![GitHub Release](https://img.shields.io/github/v/release/cirreum/Cirreum.RemoteConnections.WebSockets?style=flat-square&labelColor=1F1F1F&color=FF3B2E)](https://github.com/cirreum/Cirreum.RemoteConnections.WebSockets/releases)
[![License](https://img.shields.io/badge/license-MIT-F2F2F2?style=flat-square&labelColor=1F1F1F)](https://github.com/cirreum/Cirreum.RemoteConnections.WebSockets/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-003D8F?style=flat-square&labelColor=1F1F1F)](https://dotnet.microsoft.com/)

**Raw WebSocket transport for long-lived Cirreum client connections**

## Overview

**Cirreum.RemoteConnections.WebSockets** is the raw WebSocket implementation of Cirreum's `IRemoteConnection` abstraction — a typed, lifecycle-managed client connection backed by `ClientWebSocket`.

Raw WebSockets are the right transport when the wire format belongs to someone else — telephony media
streams, realtime speech APIs, agent host sockets — or when a service needs a long-lived outbound
channel with minimal overhead. The package supplies what the platform does not: a receive loop with
multi-frame accumulation, a reconnect loop with capped jittered backoff, token refresh per attempt,
observable state, and deterministic disposal.

Message routing is a derived-class concern. The default seam decodes Cirreum's
`{ "method", "payload" }` envelope; bridging a third-party protocol means overriding it and owning
the discriminator.

## Usage

Derive a connection type. The framework-supplied context is its first constructor parameter;
anything else resolves from the container as usual:

```csharp
public sealed class VoiceConnection(WebSocketRemoteConnectionContext context)
    : WebSocketRemoteConnection(context) {

    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct = default) =>
        this.SendBytesAsync(pcm, WebSocketMessageType.Binary, ct);

    // The provider owns this wire format, so this connection reads the frames itself
    // rather than through the Cirreum envelope.
    protected override ValueTask OnFrameReceivedAsync(
        ReadOnlyMemory<byte> payload, WebSocketMessageType messageType, CancellationToken ct) {

        // decode the provider's protocol
        return ValueTask.CompletedTask;
    }

}
```

For a Cirreum server on the other end, the default routing already matches what it sends —
register handlers by method name and send through the envelope:

```csharp
public sealed class NotificationConnection(WebSocketRemoteConnectionContext context)
    : WebSocketRemoteConnection(context) {

    public IDisposable OnNotice(Func<Notice, Task> handler) => this.On("Notice", handler);

    public Task AcknowledgeAsync(string id, CancellationToken ct = default) =>
        this.SendAsync("Acknowledge", id, ct);

}
```

Register it, and connect when the caller is ready:

```csharp
services.AddSingleton(sp => new VoiceConnection(
    WebSocketRemoteConnectionContext.Create(sp, new RemoteConnectionOptions("MyApp") {
        EndpointUri = new Uri("wss://provider.example.com/realtime")
    })));
```

For a connection whose lifetime is a session rather than the application — one per phone call,
one per bridge — construct one per session and dispose it with the session:

```csharp
await using var voice = new VoiceConnection(
    WebSocketRemoteConnectionContext.Create(services, options));

await voice.ConnectAsync(ct);
```

Applications composing through a Cirreum application builder normally register through the matching
Runtime Extensions package instead, which reduces both shapes to a single builder call and owns the
per-session lifetime rather than leaving each application to construct and track its own.

## What the base owns

- **Receive loop** — assembles multi-frame messages and hands each complete message to
  `OnFrameReceivedAsync`. A fault in that method is logged, not fatal to the connection.
- **Reconnect loop** — raw WebSockets have none of their own. Retries indefinitely with capped,
  jittered backoff, driving the same state machine. Override `OnReconnectedAsync` to restore
  server-side state that does not survive a reconnect.
- **Credentials** — resolved on every connect *and reconnect* attempt. A `ClientWebSocket` is
  single-use, so each attempt builds a fresh one and re-reads the token; refresh is a
  consequence of that rather than extra machinery. Postures resolve in order: an explicit
  callback, an explicit authorization header, an explicit choice to connect without
  credentials, then an ambient `IRemoteConnectionTokenSource`. With none of those the
  connection fails rather than connecting anonymously.
- **State and identity** — `State` and `StateChanged` report the connection's lifecycle;
  `ConnectionId` is assigned by the adapter and stable across reconnects. `SubProtocol` reports
  what the server selected, re-read per connection because each reconnect negotiates afresh.
- **Sends are serialized** — a WebSocket permits one write at a time, so concurrent callers
  queue rather than corrupting the stream.

There is no request/response: the transport provides none, and synthesizing one would mean
rebuilding correlation, timeouts and cancellation over a one-way pipe.

## Documentation

- [CHANGELOG](docs/CHANGELOG.md)
- [Backlog](docs/BACKLOG.md)

## Contribution Guidelines

1. **Be conservative with new abstractions**  
   The API surface must remain stable and meaningful.

2. **Limit dependency expansion**  
   Only add foundational, version-stable dependencies.

3. **Favor additive, non-breaking changes**  
   Breaking changes ripple through the entire ecosystem.

4. **Include thorough unit tests**  
   All primitives and patterns should be independently testable.

5. **Document architectural decisions**  
   Context and reasoning should be clear for future maintainers.

6. **Follow .NET conventions**  
   Use established patterns from Microsoft.Extensions.* libraries.

## Versioning

Cirreum.RemoteConnections.WebSockets follows [Semantic Versioning](https://semver.org/):

- **Major** - Breaking API changes
- **Minor** - New features, backward compatible
- **Patch** - Bug fixes, backward compatible

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Cirreum Foundation Framework**  
*Layered simplicity for modern .NET*