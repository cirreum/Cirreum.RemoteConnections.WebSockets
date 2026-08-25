namespace Cirreum.RemoteConnections.WebSockets.Tests;

using Microsoft.Extensions.DependencyInjection;

public class WebSocketRemoteConnectionContextTests {

	private sealed class StubConnection(WebSocketRemoteConnectionContext context)
		: WebSocketRemoteConnection(context);

	private static IServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

	private static WebSocketRemoteConnectionContext Create(RemoteConnectionOptions options) =>
		WebSocketRemoteConnectionContext.Create<StubConnection>(Services(), options);

	private static RemoteConnectionOptions Valid() =>
		new("TestApp", new Uri("wss://example.test/media"));

	// Validation happens at registration, not at first connect ————————

	[Fact]
	public void MissingEndpoint_IsRejected() {
		((Action)(() => Create(new RemoteConnectionOptions("App"))))
			.Should().Throw<InvalidOperationException>().WithMessage("*EndpointUri*");
	}

	[Fact]
	public void RelativeEndpoint_IsRejected() {
		var options = new RemoteConnectionOptions("App") { EndpointUri = new Uri("/media", UriKind.Relative) };

		((Action)(() => Create(options)))
			.Should().Throw<InvalidOperationException>().WithMessage("*absolute*");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void NonPositiveReconnectCeiling_IsRejected(int seconds) {
		var options = Valid();
		options.ReconnectMaxDelay = TimeSpan.FromSeconds(seconds);

		((Action)(() => Create(options)))
			.Should().Throw<InvalidOperationException>().WithMessage("*ReconnectMaxDelay*");
	}

	[Fact]
	public void NullArguments_AreRejected() {
		((Action)(() => WebSocketRemoteConnectionContext.Create<StubConnection>(null!, Valid())))
			.Should().Throw<ArgumentNullException>();

		((Action)(() => WebSocketRemoteConnectionContext.Create<StubConnection>(Services(), null!)))
			.Should().Throw<ArgumentNullException>();
	}

	// Construction ——————————————————————————————————————————————

	[Fact]
	public void ValidOptions_ProduceAContext() {
		var options = Valid();

		var context = Create(options);

		context.Options.Should().BeSameAs(options);
		context.Logger.Should().NotBeNull();
		context.ConnectionId.Should().NotBeNullOrWhiteSpace();
		context.SocketFactory.Should().NotBeNull();
	}

	[Fact]
	public void EachContext_GetsItsOwnConnectionId() {
		Create(Valid()).ConnectionId.Should().NotBe(Create(Valid()).ConnectionId);
	}

	// The socket factory mints a fresh socket per attempt ————————————

	[Fact]
	public async Task TheFactory_ReturnsANewSocketEachTime() {
		// A ClientWebSocket is single-use, so a reconnect must build another one; reusing an
		// instance would hand every attempt after the first a disposed socket.
		var options = Valid();
		options.AuthorizationHeader = AuthorizationHeaderSettings.None;
		var factory = Create(options).SocketFactory;

		var (first, _) = await factory.CreateAsync(CancellationToken.None);
		var (second, _) = await factory.CreateAsync(CancellationToken.None);

		try {
			second.Should().NotBeSameAs(first);
		} finally {
			first.Dispose();
			second.Dispose();
		}
	}

	[Fact]
	public async Task TheFactory_ResolvesTheCredentialPerAttempt() {
		var calls = 0;
		var options = Valid();
		options.CredentialProvider = _ => {
			calls++;
			return ValueTask.FromResult<AuthorizationHeaderSettings?>(
				new AuthorizationHeaderSettings { Scheme = "Bearer", Value = "token" });
		};

		var factory = Create(options).SocketFactory;

		(await factory.CreateAsync(CancellationToken.None)).Socket.Dispose();
		(await factory.CreateAsync(CancellationToken.None)).Socket.Dispose();

		calls.Should().Be(2, "a reconnect must re-read the credential rather than reuse a captured one");
	}

	[Fact]
	public async Task NoPostureAndNoRegisteredSource_ThrowsOnCreate() {
		var factory = Create(Valid()).SocketFactory;

		var act = async () => await factory.CreateAsync(CancellationToken.None);

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.WithMessage("*No credentials are available*")
			.WithMessage("*wss://example.test/media*");
	}

	[Fact]
	public async Task ExplicitlyPublic_ConnectsWithoutCredentials() {
		var options = Valid();
		options.AuthorizationHeader = AuthorizationHeaderSettings.None;

		var (socket, uri) = await Create(options).SocketFactory.CreateAsync(CancellationToken.None);

		try {
			uri.Should().Be(options.EndpointUri, "no credential means no query parameter is appended");
		} finally {
			socket.Dispose();
		}
	}

	[Fact]
	public async Task ConfigureTransport_RunsForEveryAttempt() {
		var invoked = 0;
		var options = Valid();
		options.AuthorizationHeader = AuthorizationHeaderSettings.None;

		var context = WebSocketRemoteConnectionContext.Create<StubConnection>(
			Services(), options, _ => invoked++);

		(await context.SocketFactory.CreateAsync(CancellationToken.None)).Socket.Dispose();
		(await context.SocketFactory.CreateAsync(CancellationToken.None)).Socket.Dispose();

		invoked.Should().Be(2, "each attempt builds a fresh socket that must be configured too");
	}

}
