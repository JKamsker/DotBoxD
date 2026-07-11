using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Shared;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.EndToEndCoverageTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Full-stack end-to-end coverage that drives the generated GameService proxy + dispatcher through
/// the complete <see cref="RpcPeer"/>/<see cref="RpcHost"/> + framing/transport stack over three real
/// transports: TCP loopback, named pipes, and the in-memory pipe. These intentionally overlap the
/// existing integration suites but use distinct names and exercise additional error/cancellation/
/// concurrency/large-payload/transformer paths that the happy-path suites do not.
/// </summary>
public sealed class EndToEndTransportRoundTripTests
{
    // ---------------------------------------------------------------------------------------------
    // Happy-path round trips across all three transports — register -> get state -> move -> action.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task FullPlayerLifecycle_OverTcp_ReturnsCorrectModels()
    {
        await using var h = await EndToEndTransportHarness.StartTcpAsync(
            peer => peer.ProvideGameService(new TestGameService()));

        await RunFullLifecycleAsync(h.Game);
    }

    [Fact]
    public async Task FullPlayerLifecycle_OverNamedPipe_ReturnsCorrectModels()
    {
        await using var h = await EndToEndTransportHarness.StartNamedPipeAsync(
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
        var registered = await game.RegisterPlayerAsync("E2E-Hero").WaitAsync(EndToEndTimeout);
        Assert.Equal("E2E-Hero", registered.Name);
        Assert.Equal(1, registered.Level);
        Assert.Equal(100, registered.Health);
        Assert.NotEmpty(registered.PlayerId);

        var status = await game.GetServerStatusAsync().WaitAsync(EndToEndTimeout);
        Assert.Equal("1.0.0-test", status.Version);
        Assert.Equal(1, status.PlayerCount);

        var move = await game.MovePlayerAsync(new MoveRequest
        {
            PlayerId = registered.PlayerId,
            X = 11,
            Y = 22,
            Z = 33
        }).WaitAsync(EndToEndTimeout);
        Assert.True(move.Success);

        var state = await game.GetPlayerStateAsync(new PlayerId { Id = registered.PlayerId }).WaitAsync(EndToEndTimeout);
        Assert.Equal(registered.PlayerId, state.PlayerId);
        Assert.Equal(11, state.PositionX);
        Assert.Equal(22, state.PositionY);
        Assert.Equal(33, state.PositionZ);

        var action = await game.PerformActionAsync(new ActionRequest
        {
            PlayerId = registered.PlayerId,
            ActionType = "Jump",
            TargetId = null
        }).WaitAsync(EndToEndTimeout);
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
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(EndToEndTimeout));

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
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(EndToEndTimeout));

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
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(EndToEndTimeout));

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
