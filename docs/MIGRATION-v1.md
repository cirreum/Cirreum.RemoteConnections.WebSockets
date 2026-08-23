# Migration to v1

Initial release — there is no prior version of **Cirreum.RemoteConnections.WebSockets** to migrate from.

The package supplies the transport implementation for the `IRemoteConnection` abstraction that ships in `Cirreum.Contracts` and `Cirreum.Domain`. Applications bridging a third-party WebSocket protocol by hand can adopt this package by overriding the frame-routing seam; the interface is unchanged.
