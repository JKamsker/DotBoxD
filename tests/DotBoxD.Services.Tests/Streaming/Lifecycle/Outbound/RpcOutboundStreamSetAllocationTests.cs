using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound;

public sealed class RpcOutboundStreamSetAllocationTests
{
    [Fact]
    public void Empty_background_disposal_does_not_allocate_work()
        => AssertBackgroundDisposalDoesNotAllocate(RpcOutboundStreamSet.Empty);

    [Fact]
    public async Task Disposed_background_disposal_does_not_allocate_work()
    {
        var manager = new RpcStreamManager(
            new MessagePackRpcSerializer(),
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);
        using var source = new MemoryStream();
        var handle = manager.ReserveOutbound(RpcStreamKind.Binary);
        var outbound = manager.RegisterOutbound(
            RpcStreamAttachment.FromStream(handle, source, leaveOpen: true),
            CancellationToken.None);
        await outbound.DisposeAsync();

        AssertBackgroundDisposalDoesNotAllocate(outbound);
        Assert.Equal(0, manager.OutboundSenderCount);
    }

    private static void AssertBackgroundDisposalDoesNotAllocate(RpcOutboundStreamSet outbound)
    {
        outbound.DisposeInBackground();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16; i++)
        {
            outbound.DisposeInBackground();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
    }
}
