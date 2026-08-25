namespace Cirreum.RemoteConnections.WebSockets.Tests;

using Microsoft.Extensions.DependencyInjection;

public class CredentialResolutionTests {

	// ---------------------------------------------------------------------
	// Harness
	// ---------------------------------------------------------------------

	private sealed class TestConnection(WebSocketRemoteConnectionContext context)
		: WebSocketRemoteConnection(context);

	private sealed class OtherConnection(WebSocketRemoteConnectionContext context)
		: WebSocketRemoteConnection(context);

	private static RemoteConnectionOptions Options() =>
		new("TestApp", new Uri("wss://example.test/media"));

	private static AuthorizationHeaderSettings Bearer(string value) =>
		new() { Scheme = "Bearer", Value = value };

	private static WebSocketConnectionFactory Factory(
		RemoteConnectionOptions options,
		IServiceProvider? services = null) =>
		WebSocketRemoteConnectionContext
			.Create<TestConnection>(services ?? new ServiceCollection().BuildServiceProvider(), options)
			.SocketFactory;

	private static async Task<Uri> ConnectUriAsync(
		RemoteConnectionOptions options,
		IServiceProvider? services = null) {

		var (socket, uri) = await Factory(options, services).CreateAsync(CancellationToken.None);
		socket.Dispose();
		return uri;

	}

	private sealed class StubSource(AuthorizationHeaderSettings? credential) : IRemoteConnectionCredentialSource {

		public RemoteConnectionTokenRequest? LastRequest { get; private set; }

		public ValueTask<AuthorizationHeaderSettings?> GetCredentialAsync(
			RemoteConnectionTokenRequest request, CancellationToken cancellationToken = default) {

			this.LastRequest = request;
			return ValueTask.FromResult(credential);

		}

	}

	// ---------------------------------------------------------------------
	// What the source is told
	// ---------------------------------------------------------------------

	[Fact]
	public async Task TheSourceIsToldTheConnectionItIsSupplyingFor() {

		var options = Options();
		options.Scopes = ["api://contoso/access_as_user"];
		var source = new StubSource(Bearer("token"));

		await ConnectUriAsync(options, Services(source));

		source.LastRequest.Should().NotBeNull();
		source.LastRequest!.EndpointUri.Should().Be(new Uri("wss://example.test/media"));
		source.LastRequest.Scopes.Should().Equal("api://contoso/access_as_user");
		source.LastRequest.ConnectionType.Should().Be<TestConnection>();

	}

	[Fact]
	public async Task DeclaringNoScopes_ReachesTheSourceAsEmptyNotNull() {

		var source = new StubSource(Bearer("token"));

		await ConnectUriAsync(Options(), Services(source));

		source.LastRequest!.Scopes.Should().BeEmpty();

	}

	// ---------------------------------------------------------------------
	// Source selection
	// ---------------------------------------------------------------------

	private static IServiceProvider Services(IRemoteConnectionCredentialSource source) =>
		new ServiceCollection().AddSingleton(source).BuildServiceProvider();

	[Fact]
	public async Task ASourceKeyedToTheConnectionType_WinsOverTheUnkeyedOne() {

		var keyed = new StubSource(Bearer("keyed"));
		var services = new ServiceCollection()
			.AddSingleton<IRemoteConnectionCredentialSource>(new StubSource(Bearer("ambient")))
			.AddKeyedSingleton<IRemoteConnectionCredentialSource>(typeof(TestConnection), (_, _) => keyed)
			.BuildServiceProvider();

		await ConnectUriAsync(Options(), services);

		keyed.LastRequest.Should().NotBeNull("the keyed source is the one that was asked");

	}

	[Fact]
	public async Task ASourceKeyedToAnotherConnection_IsNotUsed() {

		var keyed = new StubSource(Bearer("keyed"));
		var ambient = new StubSource(Bearer("ambient"));
		var services = new ServiceCollection()
			.AddSingleton<IRemoteConnectionCredentialSource>(ambient)
			.AddKeyedSingleton<IRemoteConnectionCredentialSource>(typeof(OtherConnection), (_, _) => keyed)
			.BuildServiceProvider();

		await ConnectUriAsync(Options(), services);

		ambient.LastRequest.Should().NotBeNull();
		keyed.LastRequest.Should().BeNull();

	}

	// ---------------------------------------------------------------------
	// A resolved credential's three answers
	// ---------------------------------------------------------------------

	[Fact]
	public async Task ASourceReturningNone_ConnectsWithoutACredential() {

		var uri = await ConnectUriAsync(Options(), Services(new StubSource(AuthorizationHeaderSettings.None)));

		uri.Should().Be(new Uri("wss://example.test/media"), "no credential means no query parameter");

	}

	[Fact]
	public async Task ASourceReturningNull_FailsRatherThanConnectingAnonymously() {

		// Connecting anyway would send an unauthenticated upgrade that the server refuses, which
		// reads as an application fault rather than a missing credential.
		var act = async () => await ConnectUriAsync(Options(), Services(new StubSource(null)));

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.WithMessage("*No credential was supplied*")
			.WithMessage("*wss://example.test/media*");

	}

	[Fact]
	public async Task ANonBearerCredentialFromASource_IsAccepted() {

		// Unlike the SignalR transport, a raw WebSocket builds a fresh socket per attempt and
		// sets its headers after the credential resolves, so any scheme can be resolved per
		// attempt rather than only Bearer.
		var credential = new AuthorizationHeaderSettings { Scheme = "ApiKey", Value = "abc123" };

		var uri = await ConnectUriAsync(Options(), Services(new StubSource(credential)));

		// Off-browser the credential travels as a header, so the URI is untouched.
		uri.Should().Be(new Uri("wss://example.test/media"));

	}

	[Fact]
	public async Task TheCredentialIsResolvedOnEveryAttempt() {

		var source = new CountingSource();
		var factory = Factory(Options(), Services(source));

		(await factory.CreateAsync(CancellationToken.None)).Socket.Dispose();
		(await factory.CreateAsync(CancellationToken.None)).Socket.Dispose();

		source.Calls.Should().Be(2, "a reconnect must re-read the credential rather than reuse one");

	}

	private sealed class CountingSource : IRemoteConnectionCredentialSource {

		public int Calls { get; private set; }

		public ValueTask<AuthorizationHeaderSettings?> GetCredentialAsync(
			RemoteConnectionTokenRequest request, CancellationToken cancellationToken = default) {

			this.Calls++;
			return ValueTask.FromResult<AuthorizationHeaderSettings?>(
				new AuthorizationHeaderSettings { Scheme = "Bearer", Value = $"token-{this.Calls}" });

		}

	}

}
