using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.Pooling;

public sealed class BoundedTransportOperationPoolTests
{
    [Fact]
    public void ConcurrentReturn_RetainsOneHotAndSixteenOverflowEntries()
    {
        var pool = new BoundedTransportOperationPool<CapacityPoolEntry>();
        var entries = Enumerable.Range(0, 64).Select(_ => new CapacityPoolEntry()).ToArray();

        Parallel.ForEach(entries, pool.Return);

        Assert.Equal(BoundedTransportOperationPool<CapacityPoolEntry>.MaxRetainedCount, pool.RetainedCount);
        Assert.True(pool.HasAvailable);
        var rented = Drain(pool);
        Assert.Equal(BoundedTransportOperationPool<CapacityPoolEntry>.MaxRetainedCount, rented.Count);
        Assert.Equal(rented.Count, rented.Distinct().Count());
        Assert.All(rented, entry => Assert.Contains(entry, entries));
        Assert.Equal(0, pool.RetainedCount);
        Assert.False(pool.HasAvailable);
    }

    [Fact]
    public void PooledOperation_RetainsAtMostHotSlotPlusSixteenOverflowEntries()
    {
        var count = BoundedTransportOperationPool<CapacityPoolEntry>.MaxRetainedCount + 1;
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

        Assert.Equal(BoundedTransportOperationPool<CapacityPoolEntry>.MaxRetainedCount, retained.Count);
        Assert.Equal(retained.Count, retained.Distinct().Count());
    }

    [Fact]
    public void OperationCreationReservation_StopsAtPoolCapacityAndSupportsRollback()
    {
        var capacity = BoundedTransportOperationPool<CapacityPoolEntry>.MaxRetainedCount;
        for (var index = 0; index < capacity; index++)
        {
            Assert.True(
                BoundedTransportOperationCreationBudget<ReservationTag>.TryReserve(out _));
        }

        Assert.False(
            BoundedTransportOperationCreationBudget<ReservationTag>.TryReserve(out _));

        BoundedTransportOperationCreationBudget<ReservationTag>.CancelReservation();

        Assert.True(BoundedTransportOperationCreationBudget<ReservationTag>.TryReserve(out _));
        Assert.False(BoundedTransportOperationCreationBudget<ReservationTag>.TryReserve(out _));
    }

    [Fact]
    public void ConcurrentOperationCreationReservation_NeverExceedsPoolCapacity()
    {
        var reservations = 0;
        Parallel.For(0, 64, iteration =>
        {
            if (BoundedTransportOperationCreationBudget<ConcurrentReservationTag>.TryReserve(
                    out _))
            {
                Interlocked.Increment(ref reservations);
            }
        });

        Assert.Equal(
            BoundedTransportOperationPool<CapacityPoolEntry>.MaxRetainedCount,
            reservations);
        Assert.False(
            BoundedTransportOperationCreationBudget<ConcurrentReservationTag>.TryReserve(out _));
    }

    private static List<CapacityPoolEntry> Drain(
        BoundedTransportOperationPool<CapacityPoolEntry> pool)
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
    private sealed class ConcurrentReservationTag;
    private sealed class ReservationTag;
}
