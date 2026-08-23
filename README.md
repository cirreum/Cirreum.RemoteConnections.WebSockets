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

Apps install the runtime extension, not this package directly:
`Cirreum.Runtime.RemoteConnections.WebSockets`.

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