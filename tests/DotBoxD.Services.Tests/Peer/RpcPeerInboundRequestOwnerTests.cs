using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer.Inbound;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Tests.Protocol.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer;

[Collection(PooledBufferWriterCacheCollection.Name)]
public sealed class RpcPeerInboundRequestOwnerTests
{
    [Fact]
    public void MaterializePayloadFrame_PreservesBodyAndInvalidatesWriterAliases()
    {
        var writer = PooledBufferWriter.Rent(4);
        new byte[] { 10, 20, 30, 40 }.CopyTo(writer.GetSpan(4));
        writer.Advance(4);
        var frame = new RpcFrame(writer);
        var inbound = new RpcPeerInboundRequest(
            frame,
            new RpcRequest(),
            messageId: 7,
            frame.Memory.Slice(1, 2),
            requestCts: null,
            dispatcher: null,
            requiresStreamingContext: false);

        var materialized = inbound.MaterializePayloadFrame();
        try
        {
            Assert.False(materialized.Frame.IsWriterBacked);
            Assert.Equal(new byte[] { 20, 30 }, materialized.Body.ToArray());
            Assert.Throws<ObjectDisposedException>(() => frame.Memory);
        }
        finally
        {
            materialized.Frame.Dispose();
            frame.Dispose();
        }
    }
}
