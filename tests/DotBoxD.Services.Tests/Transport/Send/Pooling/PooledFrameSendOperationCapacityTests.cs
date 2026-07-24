using System.Runtime.CompilerServices;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.Pooling;

public sealed class PooledFrameSendOperationCapacityTests
{
    [Fact]
    public async Task PopulationAndRetainedPool_AreBoundedAtSharedCapacity()
    {
        var capacity = BoundedTransportOperationPool<object>.MaxRetainedCount;
        var operations = new List<TestPooledFrameSendOperation<CapacityTag>>(capacity);
        var sources = new List<ControlledPendingSend>(capacity);
        var sends = new List<ValueTask>(capacity);
        for (var index = 0; index < capacity; index++)
        {
            var operation = TestPooledFrameSendOperation<CapacityTag>.RentOrCreate();
            Assert.NotNull(operation);
            var source = new ControlledPendingSend();
            operations.Add(operation);
            sources.Add(source);
            sends.Add(operation!.Issue(source.Pending));
        }

        Assert.Equal(capacity, TestPooledFrameSendOperation<CapacityTag>.CreatedCount);
        Assert.False(TestPooledFrameSendOperation<CapacityTag>.HasAvailable);
        Assert.Null(TestPooledFrameSendOperation<CapacityTag>.RentOrCreate());

        foreach (var source in sources)
        {
            source.Succeed();
        }

        foreach (var send in sends)
        {
            await send;
        }

        Assert.Equal(capacity, TestPooledFrameSendOperation<CapacityTag>.RetainedCount);
        var rented = Enumerable.Range(0, capacity)
            .Select(_ => TestPooledFrameSendOperation<CapacityTag>.RentOrCreate())
            .ToArray();
        Assert.All(rented, Assert.NotNull);
        Assert.Equal(capacity, rented.Distinct().Count());
        Assert.Null(TestPooledFrameSendOperation<CapacityTag>.RentOrCreate());
    }

    [Fact]
    public void CompletedUnconsumedLeases_ClearGraphsAndExhaustBudgetWithoutGrowing()
    {
        var capacity = BoundedTransportOperationPool<object>.MaxRetainedCount;
        var abandoned = Enumerable.Range(0, capacity)
            .Select(_ => CreateAbandonedLease<AbandonmentTag>())
            .ToArray();

        Assert.All(abandoned, static lease => Assert.True(lease.Send.IsCompletedSuccessfully));
        Assert.Equal(capacity, TestPooledFrameSendOperation<AbandonmentTag>.CreatedCount);
        Assert.Equal(0, TestPooledFrameSendOperation<AbandonmentTag>.RetainedCount);
        Assert.Null(TestPooledFrameSendOperation<AbandonmentTag>.RentOrCreate());

        ForceGc();

        Assert.All(abandoned, static lease => Assert.False(lease.RetainedGraph.IsAlive));
        GC.KeepAlive(abandoned);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AbandonedLease CreateAbandonedLease<TTag>()
    {
        var operation = TestPooledFrameSendOperation<TTag>.RentOrCreate()
            ?? throw new InvalidOperationException("The isolated send-operation population is exhausted.");
        var retainedGraph = new RetainedGraph();
        var weakGraph = new WeakReference(retainedGraph);
        var source = new ControlledPendingSend();
        var send = operation.Issue(source.Pending, retainedGraph);
        source.Succeed();
        return new AbandonedLease(send, weakGraph);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct AbandonedLease(ValueTask Send, WeakReference RetainedGraph);

    private sealed class RetainedGraph;
    private sealed class CapacityTag;
    private sealed class AbandonmentTag;
}
