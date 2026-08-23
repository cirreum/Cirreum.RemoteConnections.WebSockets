# Cirreum.RemoteConnections.WebSockets 1.0.0

First release of the raw WebSocket transport for the Cirreum RemoteConnections track.

Raw WebSockets are the right transport when the wire format belongs to someone else — telephony media streams, realtime speech APIs, agent host sockets — and when a service needs a long-lived outbound channel of its own. The package supplies the machinery the platform does not: a receive loop with multi-frame accumulation, a reconnect loop with capped jittered backoff, token refresh per attempt, observable state, and deterministic disposal.

Register with `AddRemoteConnection<TConnection>()` or `AddRemoteConnectionFactory<TConnection>()` from `Cirreum.Runtime.RemoteConnections.WebSockets`.
