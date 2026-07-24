using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.Pooling;

public sealed class PooledFrameSendOperationCompletionTests
{
    [Fact]
    public async Task Success_ClearsStateBeforePublicationAndRecyclesAfterConsumption()
    {
        var operation = RequireOperation<SuccessTag>();
        var source = new ControlledPendingSend();
        var retainedState = new object();
        var send = operation.Issue(source.Pending, retainedState);

        Assert.False(send.IsCompleted);
        source.Succeed();

        Assert.True(send.IsCompletedSuccessfully);
        Assert.Null(operation.RetainedState);
        Assert.Equal(1, operation.ClearCount);
        Assert.Null(TestPooledFrameSendOperation<SuccessTag>.TryTakeRecycled());

        await send;
        var recycled = TestPooledFrameSendOperation<SuccessTag>.TryTakeRecycled();
        Assert.Same(operation, recycled);
        Assert.Null(TestPooledFrameSendOperation<SuccessTag>.TryTakeRecycled());
    }

    [Fact]
    public async Task SuccessFaultAndCancellation_RemainReusable()
    {
        await VerifyCompletionAndReuse<ReusableSuccessTag>(error: null);
        await VerifyCompletionAndReuse<ReusableFaultTag>(new IOException("send failed"));
        await VerifyCompletionAndReuse<ReusableCancellationTag>(
            new OperationCanceledException(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task RegistrationFailure_IsPublishedThroughTypedHookAndRecycles()
    {
        var marker = new RegistrationFailureException();
        var source = new ThrowingRegistrationSend(marker);
        var operation = RequireOperation<RegistrationFailureTag>();
        var retainedState = new object();

        var send = operation.Issue(source.Pending, retainedState);

        Assert.True(send.IsFaulted);
        Assert.Equal(1, operation.RegistrationFailureCount);
        Assert.Null(operation.RetainedState);
        var thrown = await Assert.ThrowsAsync<RegistrationFailureException>(
            async () => await send);
        Assert.Same(marker, thrown);
        Assert.Same(
            operation,
            TestPooledFrameSendOperation<RegistrationFailureTag>.TryTakeRecycled());
    }

    [Fact]
    public async Task CleanupFailure_IsPublishedAfterReferencesClearAndOperationRemainsReusable()
    {
        var marker = new CleanupFailureException();
        var source = new ControlledPendingSend();
        var operation = RequireOperation<CleanupFailureTag>();
        var send = operation.Issue(source.Pending, new object(), cleanupError: marker);

        source.Succeed();

        Assert.Null(operation.RetainedState);
        var thrown = await Assert.ThrowsAsync<CleanupFailureException>(async () => await send);
        Assert.Same(marker, thrown);
        var reused = RequireOperation<CleanupFailureTag>();
        Assert.Same(operation, reused);
        var nextSource = new ControlledPendingSend();
        var next = reused.Issue(nextSource.Pending);
        nextSource.Succeed();
        await next;
        Assert.Same(reused, TestPooledFrameSendOperation<CleanupFailureTag>.TryTakeRecycled());
    }

    private static async Task VerifyCompletionAndReuse<TTag>(Exception? error)
    {
        var operation = RequireOperation<TTag>();
        var firstSource = new ControlledPendingSend();
        var first = operation.Issue(firstSource.Pending, new object());

        Complete(firstSource, error);

        Assert.Equal(error is null, first.IsCompletedSuccessfully);
        Assert.Equal(error is OperationCanceledException, first.IsCanceled);
        Assert.Equal(error is not null and not OperationCanceledException, first.IsFaulted);
        var observed = await Record.ExceptionAsync(async () => await first);
        if (error is null)
        {
            Assert.Null(observed);
        }
        else
        {
            Assert.Same(error, observed);
        }

        var reused = TestPooledFrameSendOperation<TTag>.RentOrCreate();
        Assert.Same(operation, reused);
        var secondSource = new ControlledPendingSend();
        var second = reused!.Issue(secondSource.Pending);
        secondSource.Succeed();
        await second;
        Assert.Equal(2, reused.ClearCount);
        Assert.Same(reused, TestPooledFrameSendOperation<TTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameSendOperation<TTag>.TryTakeRecycled());
    }

    private static void Complete(ControlledPendingSend source, Exception? error)
    {
        if (error is null)
        {
            source.Succeed();
        }
        else
        {
            source.Fail(error);
        }
    }

    private static TestPooledFrameSendOperation<TTag> RequireOperation<TTag>() =>
        TestPooledFrameSendOperation<TTag>.RentOrCreate()
        ?? throw new InvalidOperationException("The isolated send-operation population is exhausted.");

    private sealed class SuccessTag;
    private sealed class ReusableSuccessTag;
    private sealed class ReusableFaultTag;
    private sealed class ReusableCancellationTag;
    private sealed class RegistrationFailureTag;
    private sealed class CleanupFailureTag;
    private sealed class RegistrationFailureException : Exception;
    private sealed class CleanupFailureException : Exception;
}
