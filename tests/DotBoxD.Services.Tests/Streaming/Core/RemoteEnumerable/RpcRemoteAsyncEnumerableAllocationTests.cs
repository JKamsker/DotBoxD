using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;
using Xunit.Abstractions;

namespace DotBoxD.Services.Tests.Streaming.Core.RemoteEnumerable;

public sealed class RpcRemoteAsyncEnumerableAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 88)]
    [InlineData(true, 96)]
    [Trait("Category", "AllocationMeasurement")]
    public void Creating_enumerations_does_not_add_auxiliary_allocations(bool referenceValue, int budgetPerEnumeration)
    {
        var allocated = referenceValue ? Measure<string>() : Measure<int>();
        output.WriteLine($"Enumerable and enumerator allocations (reference value: {referenceValue}): {allocated} B/pair.");
        Assert.True(allocated <= budgetPerEnumeration, $"Expected at most {budgetPerEnumeration} B/pair, got {allocated}.");
    }

    [Fact]
    [Trait("Category", "AllocationMeasurement")]
    public async Task Pending_reads_do_not_add_snapshot_allocations()
    {
        var minimum = long.MaxValue;
        var serializer = new MessagePackRpcSerializer();
        for (var index = 0; index < 164; index++)
        {
            var manager = new RpcStreamManager(serializer, static (_, _) => Task.CompletedTask, exceptionTransformer: null);
            var receiver = manager.RegisterInboundResponse(new RpcStreamHandle(1, RpcStreamKind.Items), CancellationToken.None);
            await using var enumerator = new RpcRemoteAsyncEnumerable<int>(receiver, serializer).GetAsyncEnumerator();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var next = enumerator.MoveNextAsync();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.False(next.IsCompleted);
            receiver.Complete();
            Assert.False(await next);
            if (index >= 64)
            {
                // Every read must complete correctly; the minimum excludes one-time runtime
                // initialization on a newly selected worker without hiding per-read allocations.
                minimum = Math.Min(minimum, allocated);
            }
        }

        output.WriteLine($"Pending MoveNextAsync minimum after warmup: {minimum} B/read.");
        Assert.True(minimum <= 272, $"Expected at most 272 B/pending read, got {minimum}.");
    }

    private static long Measure<T>()
    {
        const int iterations = 1_000;
        var serializer = new MessagePackRpcSerializer();
        var manager = new RpcStreamManager(serializer, static (_, _) => Task.CompletedTask, exceptionTransformer: null);
        var receiver = manager.RegisterInboundResponse(new RpcStreamHandle(1, RpcStreamKind.Items), CancellationToken.None);
        var enumerables = new IAsyncEnumerable<T>[iterations];
        var enumerators = new IAsyncEnumerator<T>[iterations];
        for (var index = 0; index < 100; index++)
        {
            new RpcRemoteAsyncEnumerable<T>(receiver, serializer).GetAsyncEnumerator().DisposeAsync().GetAwaiter().GetResult();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < iterations; index++)
        {
            enumerables[index] = new RpcRemoteAsyncEnumerable<T>(receiver, serializer);
            enumerators[index] = enumerables[index].GetAsyncEnumerator();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        foreach (var enumerator in enumerators)
        {
            enumerator.DisposeAsync().GetAwaiter().GetResult();
        }

        GC.KeepAlive(enumerables);
        GC.KeepAlive(enumerators);
        return allocated / iterations;
    }
}
