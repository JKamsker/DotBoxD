using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Peer;

public sealed class RpcPeerStreamReservationLifecycleTests
{
    [Fact]
    public async Task ReserveStream_AfterDispose_ThrowsObjectDisposedException()
    {
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer());

        await peer.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => peer.ReserveStream(RpcStreamKind.Binary));
    }
}
