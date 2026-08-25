namespace Cirreum.RemoteConnections.WebSockets.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Net.WebSockets;

/// <summary>
/// Compiles the connection types and the registrations the README documents. A sample that no
/// longer matches the surface fails the build here rather than at a reader.
/// </summary>
public class ReadmeSampleTests {

	public sealed record Notice(string Id, string Text);

	// README — "Derive a connection type"
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

	// README — "For a Cirreum server on the other end"
	public sealed class NotificationConnection(WebSocketRemoteConnectionContext context)
		: WebSocketRemoteConnection(context) {

		public IDisposable OnNotice(Func<Notice, Task> handler) => this.On("Notice", handler);

		public Task AcknowledgeAsync(string id, CancellationToken ct = default) =>
			this.SendAsync("Acknowledge", id, ct);

	}

	[Fact]
	public void The_documented_registration_compiles_and_resolves() {

		var services = new ServiceCollection();

		// README — "Register it"
		services.AddSingleton(sp => new VoiceConnection(
			WebSocketRemoteConnectionContext.Create<VoiceConnection>(sp, new RemoteConnectionOptions("MyApp") {
				EndpointUri = new Uri("wss://provider.example.com/realtime"),
				Scopes = ["api://contoso/access_as_user"],
			})));

		services.Should().Contain(d => d.ServiceType == typeof(VoiceConnection));

	}

	[Fact]
	public async Task The_documented_per_session_construction_compiles() {

		var options = new RemoteConnectionOptions("MyApp") {
			EndpointUri = new Uri("wss://provider.example.com/realtime"),
			AuthorizationHeader = AuthorizationHeaderSettings.None,
		};
		var services = new ServiceCollection().BuildServiceProvider();

		// README — "For a connection whose lifetime is a session rather than the application"
		await using var voice = new VoiceConnection(
			WebSocketRemoteConnectionContext.Create<VoiceConnection>(services, options));

		voice.State.Should().Be(RemoteConnectionState.Disconnected, "construction does not connect");

	}

}
