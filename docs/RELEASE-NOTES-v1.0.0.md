# Cirreum.RemoteConnections.WebSockets 1.0.0

First release of the raw WebSocket transport for Cirreum's caller-side connection abstraction.

## What this is for

Raw WebSockets are the right transport when the wire format belongs to someone else — telephony
media streams, realtime speech APIs, agent host sockets — and when a service needs a long-lived
outbound channel of its own. SignalR is not an option there: those services do not speak it.

The platform supplies `ClientWebSocket` and nothing else. Everything a durable connection needs
around it — assembling messages, noticing a loss, reconnecting, re-presenting a credential,
reporting state — is left to each application, and that is where behaviour drifts.

This package supplies it, behind the same `IRemoteConnection` contract the SignalR transport
implements, so an application reads one connection abstraction whichever transport it uses.

## What it provides

Derive from `WebSocketRemoteConnection` and expose the endpoint's messages as typed members.

### Receive loop

Assembles multi-frame messages and hands each complete message to `OnFrameReceivedAsync` with
its type. A fault in that method is logged rather than taking the connection down, since once
overridden it is application code.

### Frame routing

`OnFrameReceivedAsync` is the seam. Its default decodes Cirreum's `{ "method", "payload" }`
envelope and dispatches to handlers registered through `On<T>` — the same envelope a Cirreum
server writes for a method-addressed push, so both ends interoperate without being configured
for it, and `SendAsync<T>` writes it on the way out.

Overriding the seam is how a connection bridges a protocol it does not own. The envelope and
the handler registry then play no part; frames arrive as bytes and the derived type owns the
discriminator. That is the case raw WebSockets exist for.

### Reconnection

The transport has none, so the connection drives it: on an abnormal close it moves to
`Reconnecting` and retries indefinitely on a capped, jittered schedule, restoring `Connected`
when an attempt succeeds. `OnReconnectedAsync` runs first, which is where server-side state
that does not survive a reconnect gets restored.

Set `Reconnect` to `false` on the options and a loss ends at `Disconnected` instead, leaving
recovery to the application.

### Credentials

A `ClientWebSocket` cannot be reconnected once closed, so every connect and reconnect attempt
builds a fresh one — and re-resolves the credential in the process. Refresh across reconnects
is therefore a consequence of the transport's own constraint rather than machinery added on
top.

Postures resolve in a fixed order: an explicit callback on the options, an explicit
authorization header, an explicit choice to connect without credentials, then an ambient
`IRemoteConnectionTokenSource`. With none of those available the connection fails rather than
connecting anonymously. Credentials travel verbatim, so a scheme prefix carried inside one
keeps routing dispatch.

Where a host cannot send headers on an upgrade — a browser — a bearer credential travels as an
`access_token` query parameter instead, which `Cirreum.Services.Server` 1.6.0 reads on a
connection endpoint. A non-bearer credential has no query equivalent and is rejected there
rather than being silently dropped.

### Binary frames

`SendBytesAsync` writes a raw frame, for audio and for protocols this connection does not
encode. Sends are serialized, because a WebSocket permits one write at a time and concurrent
callers would otherwise corrupt the stream.

### Subprotocol

`SubProtocol` reports what the server selected, read from the live socket rather than cached:
each reconnect negotiates afresh, and a connection that adapts its framing to the negotiated
protocol would otherwise carry a stale value across a reconnect. Offer protocols through the
transport configure delegate.

## Not included

Request/response. The transport provides none, and synthesizing it would mean rebuilding the
correlation, timeouts and cancellation that a protocol with real invocation semantics already
has. A connection needing that shape wants the SignalR transport.

## Requirements

* `Cirreum.Domain` 4.3.1 or later, which carries `RemoteConnectionBase` and the connection
  lifecycle hooks.
* `Cirreum.Contracts` 4.6.0 or later, which carries `IRemoteConnection`,
  `RemoteConnectionOptions` and `IRemoteConnectionTokenSource`. It flows in transitively.
* No external transport package: `ClientWebSocket` ships in the framework.
