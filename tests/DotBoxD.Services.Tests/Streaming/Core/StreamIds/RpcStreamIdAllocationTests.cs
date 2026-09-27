using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core.StreamIds;

public sealed class RpcStreamIdAllocationTests
{
    [Theory]
    [InlineData(RpcStreamKind.Binary)]
    [InlineData(RpcStreamKind.Items)]
    public void Ordinary_reservation_and_release_preserve_allocation_budget(RpcStreamKind kind)
    {
        var manager = RpcStreamIdWrapTests.NewManager();
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 20; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                var handle = manager.ReserveOutbound(kind);
                manager.ReleaseOutboundReservation(handle.StreamId);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (sample >= 10)
            {
                minimum = Math.Min(minimum, allocated);
            }
        }
        Assert.True(minimum <= 40_000, $"1,000 reservations allocated {minimum} B; budget is 40,000 B.");
        Assert.Equal(0, manager.OutboundSenderCount);
        Assert.Equal(0, manager.PendingCreditCount);
        manager.Stop();
    }
}
