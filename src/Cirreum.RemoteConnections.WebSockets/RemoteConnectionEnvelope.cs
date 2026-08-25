namespace Cirreum.RemoteServices.Connections;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The wire shape Cirreum uses to address a message to a named handler over a transport that
/// has no method concept of its own.
/// </summary>
/// <remarks>
/// This is the same envelope the server writes for a method-addressed push over a raw
/// WebSocket, so a Cirreum client and a Cirreum server interoperate without either being
/// configured for it. Bridging a third-party protocol means overriding the frame-routing seam
/// rather than adopting this shape.
/// </remarks>
internal sealed class RemoteConnectionEnvelope {

	[JsonPropertyName("method")]
	public string? Method { get; set; }

	[JsonPropertyName("payload")]
	public JsonElement Payload { get; set; }

}
