using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Peer.PeerInboundTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed class PeerInboundDispatchLifetimeCoverageTests
{
    [Fact]
    public async Task Dispose_WithInFlightUnboundedDispatch_CancelsAndDrainsCleanly()
    {
        var serializer = NewSerializer();
        var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        connection.Enqueue(CreateRequestFrame(serializer, 5, BlockingDispatcher.Service, "Hold"));

        var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions { InboundQueueCapacity = null, RequestTimeout = TimeSpan.FromMinutes(5) })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(ShortTimeout);

        // The handler is parked inside DispatchAsync. Disposing cancels the linked CTS, so the
        // handler's await throws OperationCanceledException and StopAsync drains active dispatch work
        // without surfacing the cancellation.
        await peer.DisposeAsync().AsTask().WaitAsync(ShortTimeout);
        await connection.DisposeAsync();

        Assert.False(peer.IsConnected);
    }
}
