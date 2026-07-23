using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.Pooling;

public sealed class PooledFrameSendOperationConsumptionTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task EarlyConsumption_RollsBackAndLaterConsumptionSucceeds()
    {
        var operation = RequireOperation<EarlyConsumptionTag>();
        var source = new ControlledPendingSend();
        var send = operation.Issue(source.Pending);
        var token = operation.ExpectedToken;

        Assert.Throws<InvalidOperationException>(() => operation.Consume(token));
        Assert.Null(TestPooledFrameSendOperation<EarlyConsumptionTag>.TryTakeRecycled());

        source.Succeed();
        await send;
        Assert.Same(
            operation,
            TestPooledFrameSendOperation<EarlyConsumptionTag>.TryTakeRecycled());
    }

    [Fact]
    public async Task StaleTokenAfterReuse_DoesNotConsumeCurrentLease()
    {
        var operation = RequireOperation<StaleTokenTag>();
        var firstSource = new ControlledPendingSend();
        var stale = operation.Issue(firstSource.Pending);
        var staleToken = operation.ExpectedToken;
        firstSource.Succeed();
        await stale;

        var reused = RequireOperation<StaleTokenTag>();
        Assert.Same(operation, reused);
        var currentSource = new ControlledPendingSend();
        var current = reused.Issue(currentSource.Pending);

        Assert.Throws<InvalidOperationException>(() => reused.Consume(staleToken));

        currentSource.Succeed();
        await current;
        Assert.Same(reused, TestPooledFrameSendOperation<StaleTokenTag>.TryTakeRecycled());
    }

    [Fact]
    public void SequentialDoubleConsumption_ReturnsOperationOnlyOnce()
    {
        var operation = RequireOperation<SequentialDoubleTag>();
        var source = new ControlledPendingSend();
        _ = operation.Issue(source.Pending);
        var token = operation.ExpectedToken;
        source.Succeed();

        operation.Consume(token);
        Assert.Throws<InvalidOperationException>(() => operation.Consume(token));

        Assert.Same(
            operation,
            TestPooledFrameSendOperation<SequentialDoubleTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameSendOperation<SequentialDoubleTag>.TryTakeRecycled());
    }

    [Fact]
    public async Task ConcurrentDoubleConsumption_HasOneConsumerAndOnePoolReturn()
    {
        var operation = RequireOperation<ConcurrentDoubleTag>();
        var source = new ControlledPendingSend();
        _ = operation.Issue(source.Pending);
        var token = operation.ExpectedToken;
        source.Succeed();
        using var start = new Barrier(participantCount: 2);

        var first = Task.Run(() => Consume(operation, token, start));
        var second = Task.Run(() => Consume(operation, token, start));
        var results = await Task.WhenAll(first, second).WaitAsync(Guard);

        Assert.Single(results, static error => error is null);
        Assert.Single(results, static error => error is InvalidOperationException);
        Assert.Same(
            operation,
            TestPooledFrameSendOperation<ConcurrentDoubleTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameSendOperation<ConcurrentDoubleTag>.TryTakeRecycled());
    }

    [Fact]
    public void InlineConsumer_DoesNotRecycleUntilProducerPublicationUnwinds()
    {
        var operation = RequireOperation<InlineConsumerTag>();
        var source = new ControlledPendingSend();
        var send = operation.Issue(source.Pending);
        var completion = new InlineCompletion();
#pragma warning disable xUnit1030 // The raw continuation pins producer/consumer ordering.
        var awaiter = send.ConfigureAwait(false).GetAwaiter();
#pragma warning restore xUnit1030
        awaiter.UnsafeOnCompleted(() =>
        {
            awaiter.GetResult();
            completion.Ran = true;
            completion.WasPooledDuringContinuation =
                TestPooledFrameSendOperation<InlineConsumerTag>.TryTakeRecycled() is not null;
        });

        source.Succeed();

        Assert.True(completion.Ran);
        Assert.False(completion.WasPooledDuringContinuation);
        Assert.Same(
            operation,
            TestPooledFrameSendOperation<InlineConsumerTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameSendOperation<InlineConsumerTag>.TryTakeRecycled());
    }

    private static Exception? Consume<TTag>(
        TestPooledFrameSendOperation<TTag> operation,
        short token,
        Barrier start)
    {
        start.SignalAndWait();
        return Record.Exception(() => operation.Consume(token));
    }

    private static TestPooledFrameSendOperation<TTag> RequireOperation<TTag>() =>
        TestPooledFrameSendOperation<TTag>.RentOrCreate()
        ?? throw new InvalidOperationException("The isolated send-operation population is exhausted.");

    private sealed class InlineCompletion
    {
        public bool Ran { get; set; }

        public bool WasPooledDuringContinuation { get; set; }
    }

    private sealed class EarlyConsumptionTag;
    private sealed class StaleTokenTag;
    private sealed class SequentialDoubleTag;
    private sealed class ConcurrentDoubleTag;
    private sealed class InlineConsumerTag;
}
