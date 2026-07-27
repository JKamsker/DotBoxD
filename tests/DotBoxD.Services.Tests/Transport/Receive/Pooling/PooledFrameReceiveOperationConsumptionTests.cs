using System.Threading.Tasks.Sources;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.Pooling;

public sealed class PooledFrameReceiveOperationConsumptionTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public void ProducerFirst_RecyclesOnlyAfterConsumptionAndClearsRetainedState()
    {
        var operation = TestPooledFrameReceiveOperation<ProducerFirstTag>.Rent();
        var retainedState = new object();
        _ = operation.Issue(retainedState);
        var token = operation.ExpectedVersion;

        operation.Succeed();

        Assert.Same(retainedState, operation.RetainedState);
        Assert.Null(TestPooledFrameReceiveOperation<ProducerFirstTag>.TryTakeRecycled());
        using var frame = operation.Consume(token);
        Assert.Null(operation.RetainedState);
        Assert.Equal(1, operation.ClearCount);

        var recycled = TestPooledFrameReceiveOperation<ProducerFirstTag>.TryTakeRecycled();
        Assert.Same(operation, recycled);
        Assert.Null(recycled!.RetainedState);
        Assert.Null(TestPooledFrameReceiveOperation<ProducerFirstTag>.TryTakeRecycled());
    }

    [Fact]
    public void PendingGetResult_RollsBackLeaseAndLaterConsumptionSucceeds()
    {
        var operation = TestPooledFrameReceiveOperation<PendingRollbackTag>.Rent();
        _ = operation.Issue();
        var token = operation.ExpectedVersion;

        Assert.Throws<InvalidOperationException>(() => operation.Consume(token));
        Assert.Equal(0, operation.ClearCount);

        operation.Succeed();
        using var frame = operation.Consume(token);
        Assert.Same(
            operation,
            TestPooledFrameReceiveOperation<PendingRollbackTag>.TryTakeRecycled());
    }

    [Fact]
    public void StaleTokenAfterActualReuse_DoesNotConsumeCurrentLease()
    {
        var operation = TestPooledFrameReceiveOperation<StaleTokenTag>.Rent();
        _ = operation.Issue();
        var staleToken = operation.ExpectedVersion;
        operation.Succeed();
        operation.Consume(staleToken).Dispose();

        var reused = TestPooledFrameReceiveOperation<StaleTokenTag>.Rent();
        Assert.Same(operation, reused);
        _ = reused.Issue();
        var currentToken = reused.ExpectedVersion;

        Assert.Throws<InvalidOperationException>(() => operation.Consume(staleToken));

        reused.Succeed();
        reused.Consume(currentToken).Dispose();
        Assert.Same(
            reused,
            TestPooledFrameReceiveOperation<StaleTokenTag>.TryTakeRecycled());
    }

    [Fact]
    public void SequentialDoubleGetResult_ReturnsOperationOnlyOnce()
    {
        var operation = TestPooledFrameReceiveOperation<SequentialDoubleTag>.Rent();
        _ = operation.Issue();
        var token = operation.ExpectedVersion;
        operation.Succeed();

        operation.Consume(token).Dispose();
        Assert.Throws<InvalidOperationException>(() => operation.Consume(token));

        Assert.Same(
            operation,
            TestPooledFrameReceiveOperation<SequentialDoubleTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameReceiveOperation<SequentialDoubleTag>.TryTakeRecycled());
    }

    [Fact]
    public async Task ConcurrentDoubleGetResult_HasOneConsumerAndOnePoolReturn()
    {
        var operation = TestPooledFrameReceiveOperation<ConcurrentDoubleTag>.Rent();
        _ = operation.Issue();
        var token = operation.ExpectedVersion;
        operation.Succeed();
        using var start = new Barrier(2);

        var firstResult = Task.Run(() => Consume(operation, token, start));
        var secondResult = Task.Run(() => Consume(operation, token, start));
        var results = await Task.WhenAll(firstResult, secondResult).WaitAsync(Guard);

        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result is InvalidOperationException);
        Assert.Same(
            operation,
            TestPooledFrameReceiveOperation<ConcurrentDoubleTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameReceiveOperation<ConcurrentDoubleTag>.TryTakeRecycled());
    }

    [Fact]
    public async Task ConcurrentProducerAndConsumer_ReturnOperationExactlyOnce()
    {
        var operation = TestPooledFrameReceiveOperation<ConcurrentCompletionTag>.Rent();
        _ = operation.Issue();
        var source = (IValueTaskSource<RpcFrame>)operation;
        var token = operation.ExpectedVersion;
        var consume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ConcurrentCompletionContext(consume, consumed);

        source.OnCompleted(
            static state =>
            {
                var completion = (ConcurrentCompletionContext)state!;
                completion.Consume.TrySetResult();
                completion.Consumed.Task.GetAwaiter().GetResult();
            },
            context,
            token,
            ValueTaskSourceOnCompletedFlags.None);

        var consumer = Task.Run(async () =>
        {
            await consume.Task;
            try
            {
                operation.Consume(token).Dispose();
                Assert.Null(
                    TestPooledFrameReceiveOperation<ConcurrentCompletionTag>.TryTakeRecycled());
            }
            finally
            {
                consumed.TrySetResult();
            }
        });
        var producer = Task.Run(operation.Succeed);

        await Task.WhenAll(consumer, producer).WaitAsync(Guard);
        Assert.Same(
            operation,
            TestPooledFrameReceiveOperation<ConcurrentCompletionTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameReceiveOperation<ConcurrentCompletionTag>.TryTakeRecycled());
    }

    private static Exception? Consume(
        TestPooledFrameReceiveOperation<ConcurrentDoubleTag> operation,
        short token,
        Barrier start)
    {
        start.SignalAndWait();
        try
        {
            operation.Consume(token).Dispose();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private sealed record ConcurrentCompletionContext(
        TaskCompletionSource Consume,
        TaskCompletionSource Consumed);

    private sealed class ProducerFirstTag;
    private sealed class PendingRollbackTag;
    private sealed class StaleTokenTag;
    private sealed class SequentialDoubleTag;
    private sealed class ConcurrentDoubleTag;
    private sealed class ConcurrentCompletionTag;
}
