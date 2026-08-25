# Cirreum.RemoteConnections.WebSockets v1 → v2 Migration

v2 follows `Cirreum.Contracts` 5.0.0 and `Cirreum.Domain` 5.0.0. Three mechanical changes, plus one
behavioural change worth reading before deploying — it is not a compile error.

---

## 1. Namespace

| v1 | v2 |
| --- | --- |
| `using Cirreum.RemoteServices;` | `using Cirreum.RemoteServices.Connections;` |

`WebSocketRemoteConnection`, `WebSocketRemoteConnectionContext` and the connection contracts all
moved. `AuthorizationHeaderSettings` and `RemoteIdentityConstants` did not — a file touching both
imports both namespaces.

A remote service is something you *call*; a remote connection is something you *hold open*. The
second is a relationship with a remote service rather than a peer of one, so it nests.

## 2. `Create` is generic

```csharp
// v1
WebSocketRemoteConnectionContext.Create(sp, options)

// v2
WebSocketRemoteConnectionContext.Create<VoiceConnection>(sp, options)
```

A credential source may now be registered against a connection's type, and this package is where the
source is resolved, so the type has to reach it.

Applications registering through `Cirreum.Runtime.RemoteConnections.WebSockets` are unaffected — the
registration passes the type for them.

## 3. The credential seam

| v1 | v2 |
| --- | --- |
| `IRemoteConnectionTokenSource` | `IRemoteConnectionCredentialSource` |
| `GetAccessTokenAsync(CancellationToken)` | `GetCredentialAsync(RemoteConnectionTokenRequest, CancellationToken)` |
| returns `ValueTask<string?>` | returns `ValueTask<AuthorizationHeaderSettings?>` |
| `options.AccessTokenProvider` | `options.CredentialProvider` |

```csharp
// v1 — the callback's return was assumed to be a bearer token
options.AccessTokenProvider = async ct => await this.GetTokenAsync(ct);

// v2 — the callback states the scheme
options.CredentialProvider = async ct =>
    new AuthorizationHeaderSettings { Scheme = "Bearer", Value = await this.GetTokenAsync(ct) };
```

The full before/after is in `Cirreum.Contracts`' `MIGRATION-v5.md` — one guide for the pair.

## 4. ⚠️ A callback or source returning nothing now fails the attempt

**This is behavioural, not a compile error.**

In v1, a callback or ambient source returning `null` produced a socket with no credential. The
upgrade went out unauthenticated, the server refused it, and the failure surfaced in the application
as an authentication problem with no indication that the credential was the cause.

In v2 a resolved credential has three answers:

| Return | Meaning |
| --- | --- |
| a populated `AuthorizationHeaderSettings` | present this credential |
| `AuthorizationHeaderSettings.None` | connect without one, deliberately |
| `null` | none is available — the attempt fails, naming the endpoint |

If a connection deliberately connects anonymously, say so: set `options.AuthorizationHeader` to
`AuthorizationHeaderSettings.None`, or return it from the source. Relying on `null` to mean the same
thing no longer works, and that is the point — the two were indistinguishable.

## New capabilities

### Scopes, and a source per connection

```csharp
options.Scopes = ["api://contoso/access_as_user"];

services.AddKeyedScoped<IRemoteConnectionCredentialSource, PartnerCredentialSource>(typeof(PartnerConnection));
```

The source is told the endpoint, the declared scopes, and the connection type. A source registered
keyed to that type is preferred over the unkeyed one.

### Any scheme, resolved per attempt

A fresh socket is built per attempt and its headers are set *after* the credential resolves, so an
ApiKey or other non-Bearer scheme refreshes across reconnects exactly as a token does. In v1 only a
static header could carry a non-Bearer credential.

This differs from the SignalR transport, where only Bearer can be resolved per attempt — that client
copies its configured headers before the credential callback runs. The two transports differ because
their mechanisms do, not by choice.

## What didn't change

- The posture order: an explicit callback, then an explicit header, then an explicit `None`, then
  the ambient source.
- The browser path: a credential travels as an `access_token` query parameter, and only Bearer has
  that equivalent, so a non-Bearer credential in a browser is rejected.
- The receive and reconnect loops, `OnFrameReceivedAsync`, the envelope, `SubProtocol`, send
  serialization, and the `configureTransport` escape hatch.
