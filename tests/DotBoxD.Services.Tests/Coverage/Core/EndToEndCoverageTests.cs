using System.Net;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Transports.NamedPipes;
using DotBoxD.Transports.Tcp;
using Shared;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Full-stack end-to-end coverage that drives the generated GameService proxy + dispatcher through
/// the complete <see cref="RpcPeer"/>/<see cref="RpcHost"/> + framing/transport stack over three real
/// transports: TCP loopback, named pipes, and the in-memory pipe. These intentionally overlap the
/// existing integration suites but use distinct names and exercise additional error/cancellation/
/// concurrency/large-payload/transformer paths that the happy-path suites do not.
/// </summary>
public sealed partial class EndToEndCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static MessagePackRpcSerializer NewSerializer() => new();

    private static RpcPeerOptions ClientOptions() =>
        new() { RequestTimeout = TimeSpan.FromSeconds(10) };

    // ---------------------------------------------------------------------------------------------
    // Transport harness: spins up a host on a real transport, connects a client peer, and hands back
    // the generated IGameService proxy. The server peer is configured via the supplied callback so
    // each test can inject its own service instance / ExceptionTransformer.
    // ---------------------------------------------------------------------------------------------

    private sealed class TransportHarness : IAsyncDisposable
    {
        private readonly RpcHost _host;
        private readonly IAsyncDisposable _clientTransport;
        private readonly RpcPeer _client;

        public IGameService Game { get; }

        private TransportHarness(RpcHost host, IAsyncDisposable clientTransport, RpcPeer client, IGameService game)
        {
            _host = host;
            _clientTransport = clientTransport;
            _client = client;
            Game = game;
        }

        public static async Task<TransportHarness> StartTcpAsync(Action<RpcPeer> configureServer)
        {
            var serverTransport = new TcpServerTransport(IPAddress.Loopback, 0);
            var host = RpcHost
                .Listen(serverTransport, NewSerializer())
                .ForEachPeer(configureServer);
            await host.StartAsync().WaitAsync(Timeout);

            var port = serverTransport.LocalEndpoint?.Port
                ?? throw new InvalidOperationException("TCP server did not expose a bound port.");

            var clientTransport = new TcpTransport("127.0.0.1", port);
            await clientTransport.ConnectAsync().WaitAsync(Timeout);
            var client = RpcPeer.Over(clientTransport.Connection!, NewSerializer(), ClientOptions()).Start();
            return new TransportHarness(host, clientTransport, client, client.GetGameService());
        }

        public static async Task<TransportHarness> StartNamedPipeAsync(Action<RpcPeer> configureServer)
        {
            var pipeName = "dotboxd-e2e-" + Guid.NewGuid().ToString("N");
            var host = RpcHost
                .Listen(new NamedPipeServerTransport(pipeName), NewSerializer())
                .ForEachPeer(configureServer);
            await host.StartAsync().WaitAsync(Timeout);

            var clientTransport = new NamedPipeClientTransport(pipeName);
            await clientTransport.ConnectAsync().WaitAsync(Timeout);
            var client = RpcPeer.Over(clientTransport.Connection!, NewSerializer(), ClientOptions()).Start();
            return new TransportHarness(host, clientTransport, client, client.GetGameService());
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            await _clientTransport.DisposeAsync();
            await _host.DisposeAsync();
        }
    }

    private static (RpcPeer Server, RpcPeer Client, IGameService Game) StartInMemoryPair(
        Action<RpcPeer> configureServer,
        int writeChunkSize = 0)
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair(writeChunkSize);
        var server = RpcPeer.Over(serverConnection, NewSerializer());
        configureServer(server);
        server.Start();

        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        return (server, client, client.GetGameService());
    }

    // ---------------------------------------------------------------------------------------------
    // Happy-path round trips across all three transports — register -> get state -> move -> action.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task FullPlayerLifecycle_OverTcp_ReturnsCorrectModels()
    {
        await using var h = await TransportHarness.StartTcpAsync(
            peer => peer.ProvideGameService(new TestGameService()));

        await RunFullLifecycleAsync(h.Game);
    }

    [Fact]
    public async Task FullPlayerLifecycle_OverNamedPipe_ReturnsCorrectModels()
    {
        await using var h = await TransportHarness.StartNamedPipeAsync(
            peer => peer.ProvideGameService(new TestGameService()));

        await RunFullLifecycleAsync(h.Game);
    }

    [Fact]
    public async Task FullPlayerLifecycle_OverInMemoryPipe_ReturnsCorrectModels()
    {
        var (server, client, game) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            await RunFullLifecycleAsync(game);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    private static async Task RunFullLifecycleAsync(IGameService game)
    {
        var registered = await game.RegisterPlayerAsync("E2E-Hero").WaitAsync(Timeout);
        Assert.Equal("E2E-Hero", registered.Name);
        Assert.Equal(1, registered.Level);
        Assert.Equal(100, registered.Health);
        Assert.NotEmpty(registered.PlayerId);

        var status = await game.GetServerStatusAsync().WaitAsync(Timeout);
        Assert.Equal("1.0.0-test", status.Version);
        Assert.Equal(1, status.PlayerCount);

        var move = await game.MovePlayerAsync(new MoveRequest
        {
            PlayerId = registered.PlayerId,
            X = 11,
            Y = 22,
            Z = 33
        }).WaitAsync(Timeout);
        Assert.True(move.Success);

        var state = await game.GetPlayerStateAsync(new PlayerId { Id = registered.PlayerId }).WaitAsync(Timeout);
        Assert.Equal(registered.PlayerId, state.PlayerId);
        Assert.Equal(11, state.PositionX);
        Assert.Equal(22, state.PositionY);
        Assert.Equal(33, state.PositionZ);

        var action = await game.PerformActionAsync(new ActionRequest
        {
            PlayerId = registered.PlayerId,
            ActionType = "Jump",
            TargetId = null
        }).WaitAsync(Timeout);
        Assert.True(action.Success);
        Assert.Contains("Jump", action.Message);
    }

    // ---------------------------------------------------------------------------------------------
    // Server-side handler throwing a typed exception: transformer OFF hides detail (InternalError),
    // transformer ON surfaces the real type and message. The TestGameService throws
    // KeyNotFoundException for unknown players, which we use as the handler exception under test.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task HandlerException_TransformerOff_SurfacesOpaqueInternalError()
    {
        var (server, client, game) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            var ex = await Assert.ThrowsAsync<RemoteServiceException>(
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(Timeout));

            // Default: detail is hidden, opaque internal error type and message.
            Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
            Assert.Equal("Internal error.", ex.Message);
            // The real handler message must NOT leak to the caller.
            Assert.DoesNotContain("ghost", ex.Message);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task HandlerException_TransformerOn_SurfacesTypedMessageToClient()
    {
        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = ex => RpcErrorInfo.FromException(ex),
        };

        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var server = RpcPeer.Over(serverConnection, NewSerializer(), serverOptions)
            .ProvideGameService(new TestGameService())
            .Start();
        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        try
        {
            var game = client.GetGameService();

            var ex = await Assert.ThrowsAsync<RemoteServiceException>(
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(Timeout));

            // Opt-in transformer exposes the real runtime type name and message.
            Assert.Equal(nameof(KeyNotFoundException), ex.RemoteExceptionType);
            Assert.Contains("ghost", ex.Message);
            Assert.Contains("not found", ex.Message);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task HandlerException_TransformerReturningNull_FallsBackToInternalError()
    {
        // A transformer that opts a specific exception out (returns null) must produce the opaque
        // default rather than leaking detail or faulting the dispatch.
        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = _ => null,
        };

        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var server = RpcPeer.Over(serverConnection, NewSerializer(), serverOptions)
            .ProvideGameService(new TestGameService())
            .Start();
        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        try
        {
            var game = client.GetGameService();

            var ex = await Assert.ThrowsAsync<RemoteServiceException>(
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(Timeout));

            Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
            Assert.Equal("Internal error.", ex.Message);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

}
