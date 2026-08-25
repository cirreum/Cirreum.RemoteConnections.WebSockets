# Cirreum.RemoteConnections.WebSockets 2.0.0 — a credential source can see what it is supplying for

## Why this release exists

The credential seam shipped in 1.0 took no parameters. A host registered one source, and every
outbound connection in the application got the same answer from it — nothing in the call
distinguished a socket to the application's own API from one to a partner service. Where the host
could not infer an audience it answered with its own defaults, which on WebAssembly are Microsoft
Graph scopes, so the credential a connection got out of the box was aimed at the wrong resource.

That was reported against the SignalR transport by the first application built on this track. The
same seam is shared, so the same fix lands here.

## What's new

**The source is told what it is supplying for.**
`IRemoteConnectionCredentialSource.GetCredentialAsync` receives a `RemoteConnectionTokenRequest` —
the endpoint, the `Scopes` the connection's options declare, and the connection type — and returns
`AuthorizationHeaderSettings?` rather than a bare token string.

An application names the audience on the options and writes no source at all:

```csharp
options.Scopes = ["api://contoso/access_as_user"];
```

**A source may be registered keyed to a connection type**, and is preferred over the unkeyed
registration for that connection — so a bridge holding one socket to a provider and another to its
own backend can give each its own mechanism.

**Any scheme may be resolved per attempt.** A `ClientWebSocket` is single-use, so every attempt
builds a fresh one and sets its headers *after* the credential resolves. An ApiKey or other
non-Bearer credential therefore refreshes across reconnects exactly as a token does.

This is where the two transports legitimately differ. The SignalR client copies its configured
headers when it builds the client for an attempt, before the credential callback runs, so only
Bearer can be resolved per attempt there. Here the ordering is the other way round, so nothing is
restricted. The difference is in the mechanisms, not in the design.

## The behavioural change to read before deploying

A resolved credential now has three answers: a value to present, `AuthorizationHeaderSettings.None`
to connect deliberately without one, and `null` meaning none is available — which **fails the
attempt**, naming the endpoint.

In 1.0 the last two were the same answer. A callback or source returning nothing produced an
unauthenticated upgrade that the server refused, which read as an application authentication bug.
Separating them is what turns a missing credential from a puzzle into a message.

If a connection is meant to be anonymous, say so with `None`.

## Compatibility

Three mechanical changes — a namespace, a generic type argument on
`WebSocketRemoteConnectionContext.Create`, and the credential seam — plus the behavioural change
above. See [MIGRATION-v2.md](MIGRATION-v2.md).

Applications registering through `Cirreum.Runtime.RemoteConnections.WebSockets` do not touch
`Create` directly, and feel this as the namespace change and the credential seam only.

The receive and reconnect loops, `OnFrameReceivedAsync`, the envelope, `SubProtocol` and send
serialization are untouched.

## See also

- `Cirreum.Contracts` 5.0.0 — the contracts, and the reasoning behind the credential shape.
- `Cirreum.RemoteConnections.SignalR` 2.0.0 — the same seam on the other transport.
