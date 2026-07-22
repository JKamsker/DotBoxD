using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.Pooling;

public sealed class BoundedFrameReceiveOperationPoolTests
{
    [Fact]
    public void ConcurrentReturn_RetainsOneHotAndSixteenOverflowEntries()
    {
        var pool = new BoundedFrameReceiveOperationPool<CapacityPoolEntry>();
        var entries = Enumerable.Range(0, 64).Select(_ => new CapacityPoolEntry()).ToArray();

        Parallel.ForEach(entries, pool.Return);

        Assert.Equal(BoundedFrameReceiveOperationPool<CapacityPoolEntry>.MaxRetainedCount, pool.RetainedCount);
        var rented = Drain(pool);
        Assert.Equal(BoundedFrameReceiveOperationPool<CapacityPoolEntry>.MaxRetainedCount, rented.Count);
        Assert.Equal(rented.Count, rented.Distinct().Count());
        Assert.All(rented, entry => Assert.Contains(entry, entries));
        Assert.Equal(0, pool.RetainedCount);
    }

    [Fact]
    public void PooledOperation_RetainsAtMostHotSlotPlusSixteenOverflowEntries()
    {
        var count = BoundedFrameReceiveOperationPool<CapacityPoolEntry>.MaxRetainedCount + 1;
        var operations = Enumerable.Range(0, count)
            .Select(_ => TestPooledFrameReceiveOperation<CapacityOperationTag>.Rent())
            .ToArray();
        var tokens = operations.Select(operation =>
        {
            _ = operation.Issue();
            return operation.ExpectedVersion;
        }).ToArray();
        Assert.Equal(operations.Length, operations.Distinct().Count());

        for (var i = 0; i < operations.Length; i++)
        {
            operations[i].Succeed();
            operations[i].Consume(tokens[i]).Dispose();
        }

        var retained = new List<TestPooledFrameReceiveOperation<CapacityOperationTag>>();
        TestPooledFrameReceiveOperation<CapacityOperationTag>? operation;
        while ((operation = TestPooledFrameReceiveOperation<CapacityOperationTag>.TryTakeRecycled()) is not null)
        {
            retained.Add(operation);
        }

        Assert.Equal(BoundedFrameReceiveOperationPool<CapacityPoolEntry>.MaxRetainedCount, retained.Count);
        Assert.Equal(retained.Count, retained.Distinct().Count());
    }

    private static List<CapacityPoolEntry> Drain(
        BoundedFrameReceiveOperationPool<CapacityPoolEntry> pool)
    {
        var entries = new List<CapacityPoolEntry>();
        CapacityPoolEntry? entry;
        while ((entry = pool.Rent()) is not null)
        {
            entries.Add(entry);
        }

        return entries;
    }

    private sealed class CapacityPoolEntry;
    private sealed class CapacityOperationTag;
}
