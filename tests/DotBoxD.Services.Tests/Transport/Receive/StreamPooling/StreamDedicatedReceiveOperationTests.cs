using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

[Collection(StreamReceiveOperationCollection.Name)]
public sealed class StreamDedicatedReceiveOperationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SharedAndSynchronousSuccess_DoNotAdmitCache()
    {
        await using (var pair = await StreamReceiveTestPair.CreateAsync())
        {
            var pending = pair.Connection.ReceiveFrameValueAsync();
            await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId: 580));
            using var frame = await pending.AsTask().WaitAsync(Guard);
            AssertFrame(frame, messageId: 580);
            Assert.False(pair.Connection.HasDedicatedReceiveCache);
        }

        var bytes = StreamReceiveTestPair.CreateFrame(messageId: 581);
        await using var synchronous = new StreamConnection(
            new MemoryStream(bytes, writable: false),
            ownsStream: true,
            frameReadIdleTimeout: Timeout.InfiniteTimeSpan);
        using var synchronousFrame = await synchronous.ReceiveFrameValueAsync();
        AssertFrame(synchronousFrame, messageId: 581);
        Assert.False(synchronous.HasDedicatedReceiveCache);
    }

    [Fact]
    public async Task OverflowSuccess_AdmitsEmptyCacheBeforeCreatingSource()
    {
        await using var population = await StreamSaturatedReceivePopulation.CreateAsync();
        var pair = population.OverflowPair;
        Assert.False(pair.Connection.HasDedicatedReceiveCache);

        await AdmitCacheAsync(pair, messageId: 600);

        for (var messageId = 601; messageId <= 602; messageId++)
        {
            var pending = pair.Connection.ReceiveFrameValueAsync();
            Assert.False(pending.IsCompleted);
            Assert.Equal(1, pair.Connection.DedicatedReceiveOperationCount);
            await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId));
            using var frame = await pending.AsTask().WaitAsync(Guard);
            AssertFrame(frame, messageId);
            await WaitForAvailableDedicatedOperationsAsync(pair.Connection, expected: 1);
        }
    }

    [Fact]
    public async Task InlineConsumer_RearmsWithSecondDedicatedSource()
    {
        await using var population = await StreamSaturatedReceivePopulation.CreateAsync();
        var pair = population.OverflowPair;
        await AdmitCacheAsync(pair, messageId: 900);

        var first = pair.Connection.ReceiveFrameValueAsync();
        Assert.False(first.IsCompleted);
        Assert.Equal(1, pair.Connection.DedicatedReceiveOperationCount);

        var successorSource = new TaskCompletionSource<ValueTask<RpcFrame>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable xUnit1030 // This regression intentionally reenters on the producer thread.
        var awaiter = first.ConfigureAwait(false).GetAwaiter();
#pragma warning restore xUnit1030
        awaiter.UnsafeOnCompleted(() =>
        {
            try
            {
                using var frame = awaiter.GetResult();
                AssertFrame(frame, messageId: 901);
                successorSource.TrySetResult(pair.Connection.ReceiveFrameValueAsync());
            }
            catch (Exception error)
            {
                successorSource.TrySetException(error);
            }
        });

        await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId: 901));
        var successor = await successorSource.Task.WaitAsync(Guard);
        Assert.False(successor.IsCompleted);
        Assert.Equal(2, pair.Connection.DedicatedReceiveOperationCount);

        await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId: 902));
        using var successorFrame = await successor.AsTask().WaitAsync(Guard);
        AssertFrame(successorFrame, messageId: 902);
    }

    [Fact]
    public async Task TwoUnconsumedResults_ExhaustFallbackAndRecoverBothLanes()
    {
        await using var population = await StreamSaturatedReceivePopulation.CreateAsync();
        var pair = population.OverflowPair;
        await AdmitCacheAsync(pair, messageId: 910);

        var first = await CompleteWithoutConsumingAsync(pair, messageId: 911);
        var second = await CompleteWithoutConsumingAsync(pair, messageId: 912);

        Assert.Equal(2, pair.Connection.DedicatedReceiveOperationCount);
        Assert.Equal(0, pair.Connection.AvailableDedicatedReceiveOperationCount);
        Assert.False(StreamFrameReceiveOperationAcquisition.CanAcquireDedicatedOperation(pair.Connection));

        var fallback = pair.Connection.ReceiveFrameValueAsync();
        Assert.False(fallback.IsCompleted);
        await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId: 913));
        using (var frame = await fallback.AsTask().WaitAsync(Guard))
        {
            AssertFrame(frame, messageId: 913);
        }

        Assert.Equal(0, pair.Connection.AvailableDedicatedReceiveOperationCount);
        using (var frame = await first)
        {
            AssertFrame(frame, messageId: 911);
        }

        Assert.Equal(1, pair.Connection.AvailableDedicatedReceiveOperationCount);
        using (var frame = await second)
        {
            AssertFrame(frame, messageId: 912);
        }

        Assert.Equal(2, pair.Connection.AvailableDedicatedReceiveOperationCount);
        var restored = pair.Connection.ReceiveFrameValueAsync();
        await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId: 914));
        using var restoredFrame = await restored.AsTask().WaitAsync(Guard);
        AssertFrame(restoredFrame, messageId: 914);
    }

    [Fact]
    public async Task Dispose_DropsCacheAndAcceptsLateDedicatedReturn()
    {
        await using var population = await StreamSaturatedReceivePopulation.CreateAsync();
        var pair = population.OverflowPair;
        await AdmitCacheAsync(pair, messageId: 920);
        var completed = await CompleteWithoutConsumingAsync(pair, messageId: 921);

        Assert.True(pair.Connection.HasDedicatedReceiveCache);
        await pair.Connection.DisposeAsync();
        Assert.False(pair.Connection.HasDedicatedReceiveCache);

        using (var frame = await completed)
        {
            AssertFrame(frame, messageId: 921);
        }

        Assert.False(pair.Connection.HasDedicatedReceiveCache);
        var error = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pair.Connection.ReceiveFrameValueAsync().AsTask());
        Assert.Equal(nameof(StreamConnection), error.ObjectName);
    }

    internal static async Task AdmitCacheAsync(StreamReceiveTestPair pair, int messageId)
    {
        Assert.False(pair.Connection.HasDedicatedReceiveCache);
        var pending = pair.Connection.ReceiveFrameValueAsync();
        Assert.False(pending.IsCompleted);
        await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId));
        using var frame = await pending.AsTask().WaitAsync(Guard);
        AssertFrame(frame, messageId);
        Assert.True(pair.Connection.HasDedicatedReceiveCache);
        Assert.Equal(0, pair.Connection.DedicatedReceiveOperationCount);
    }

    internal static async Task<ValueTask<RpcFrame>> CompleteWithoutConsumingAsync(
        StreamReceiveTestPair pair,
        int messageId)
    {
        var pending = pair.Connection.ReceiveFrameValueAsync();
        Assert.False(pending.IsCompleted);
        await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId));
        var deadline = DateTime.UtcNow + Guard;
        while (!pending.IsCompleted)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The dedicated Stream receive did not complete.");
            }

            await Task.Yield();
        }

        return pending;
    }

    internal static void AssertFrame(RpcFrame frame, int messageId)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var actual, out var type));
        Assert.Equal(messageId, actual);
        Assert.Equal(MessageType.Response, type);
    }

    private static async Task WaitForAvailableDedicatedOperationsAsync(
        StreamConnection connection,
        int expected)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (connection.AvailableDedicatedReceiveOperationCount != expected)
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Equal(expected, connection.AvailableDedicatedReceiveOperationCount);
            }

            await Task.Yield();
        }
    }
}
