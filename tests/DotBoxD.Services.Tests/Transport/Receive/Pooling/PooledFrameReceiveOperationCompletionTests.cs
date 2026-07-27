using System.Threading.Tasks.Sources;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.Pooling;

public sealed class PooledFrameReceiveOperationCompletionTests
{
    [Fact]
    public void SuccessFaultCancellationAndTimeout_ReportStatusAndRemainReusable()
    {
        VerifyStatusAndReuse<SuccessStatusTag>(null, ValueTaskSourceStatus.Succeeded);
        VerifyStatusAndReuse<FaultStatusTag>(new IOException("read failed"), ValueTaskSourceStatus.Faulted);
        VerifyStatusAndReuse<CancellationStatusTag>(
            new OperationCanceledException(new CancellationToken(canceled: true)),
            ValueTaskSourceStatus.Canceled);
        VerifyStatusAndReuse<TimeoutStatusTag>(
            new TimeoutException("read timed out"),
            ValueTaskSourceStatus.Faulted);
    }

    [Fact]
    public void InlineRawContinuation_CanConsumeAndThrowWithoutStrandingOperation()
    {
        var operation = TestPooledFrameReceiveOperation<InlineThrowTag>.Rent();
        _ = operation.Issue();
        var source = (IValueTaskSource<RpcFrame>)operation;
        var marker = new InlineContinuationException();
        var context = new InlineThrowContext(source, operation.ExpectedVersion, marker);

        source.OnCompleted(
            static state =>
            {
                var completion = (InlineThrowContext)state!;
                completion.Status = completion.Source.GetStatus(completion.Token);
                completion.Source.GetResult(completion.Token).Dispose();
                completion.Ran = true;
                completion.WasPooledDuringContinuation =
                    TestPooledFrameReceiveOperation<InlineThrowTag>.TryTakeRecycled() is not null;
                throw completion.Marker;
            },
            context,
            operation.ExpectedVersion,
            ValueTaskSourceOnCompletedFlags.None);

        var thrown = Assert.Throws<InlineContinuationException>(operation.Succeed);

        Assert.Same(marker, thrown);
        Assert.True(context.Ran);
        Assert.Equal(ValueTaskSourceStatus.Succeeded, context.Status);
        Assert.False(context.WasPooledDuringContinuation);
        var recycled = TestPooledFrameReceiveOperation<InlineThrowTag>.TryTakeRecycled();
        Assert.Same(operation, recycled);
        Assert.Equal(1, operation.ClearCount);

        _ = recycled!.Issue();
        var nextToken = recycled.ExpectedVersion;
        recycled.Succeed();
        recycled.Consume(nextToken).Dispose();
        Assert.Same(
            recycled,
            TestPooledFrameReceiveOperation<InlineThrowTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameReceiveOperation<InlineThrowTag>.TryTakeRecycled());
    }

    [Fact]
    public void InlineConsumerDuringSetException_HandlesFaultAndCancellationBeforeRecycle()
    {
        VerifyInlineException<InlineFaultTag>(
            new IOException("read failed"),
            ValueTaskSourceStatus.Faulted);
        VerifyInlineException<InlineCancellationTag>(
            new OperationCanceledException(new CancellationToken(canceled: true)),
            ValueTaskSourceStatus.Canceled);
    }

    private static void VerifyStatusAndReuse<TTag>(
        Exception? completionError,
        ValueTaskSourceStatus expectedStatus)
    {
        var operation = TestPooledFrameReceiveOperation<TTag>.Rent();
        var retainedState = new object();
        _ = operation.Issue(retainedState);
        var source = (IValueTaskSource<RpcFrame>)operation;
        var token = operation.ExpectedVersion;
        Assert.Equal(ValueTaskSourceStatus.Pending, source.GetStatus(token));

        if (completionError is null)
        {
            operation.Succeed();
        }
        else
        {
            operation.Fail(completionError);
        }

        Assert.Equal(expectedStatus, source.GetStatus(token));
        var observedError = Consume(source, token);
        if (completionError is null)
        {
            Assert.Null(observedError);
        }
        else
        {
            Assert.Same(completionError, observedError);
        }

        var recycled = TestPooledFrameReceiveOperation<TTag>.Rent();
        Assert.Same(operation, recycled);
        Assert.Null(recycled.RetainedState);

        _ = recycled.Issue();
        var nextToken = recycled.ExpectedVersion;
        recycled.Succeed();
        Assert.Null(Consume(source, nextToken));
        Assert.Same(recycled, TestPooledFrameReceiveOperation<TTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameReceiveOperation<TTag>.TryTakeRecycled());
    }

    private static void VerifyInlineException<TTag>(
        Exception expectedError,
        ValueTaskSourceStatus expectedStatus)
    {
        var operation = TestPooledFrameReceiveOperation<TTag>.Rent();
        _ = operation.Issue();
        var source = (IValueTaskSource<RpcFrame>)operation;
        var context = new InlineExceptionContext<TTag>(
            source,
            operation.ExpectedVersion);
        source.OnCompleted(
            static state =>
            {
                var completion = (InlineExceptionContext<TTag>)state!;
                completion.Status = completion.Source.GetStatus(completion.Token);
                completion.Error = Record.Exception(
                    () => completion.Source.GetResult(completion.Token).Dispose());
                completion.WasPooledDuringContinuation =
                    TestPooledFrameReceiveOperation<TTag>.TryTakeRecycled() is not null;
            },
            context,
            operation.ExpectedVersion,
            ValueTaskSourceOnCompletedFlags.None);

        operation.Fail(expectedError);

        Assert.Equal(expectedStatus, context.Status);
        Assert.Same(expectedError, context.Error);
        Assert.False(context.WasPooledDuringContinuation);
        Assert.Same(operation, TestPooledFrameReceiveOperation<TTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameReceiveOperation<TTag>.TryTakeRecycled());
    }

    private static Exception? Consume(IValueTaskSource<RpcFrame> source, short token)
    {
        try
        {
            source.GetResult(token).Dispose();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private sealed class InlineThrowContext(
        IValueTaskSource<RpcFrame> source,
        short token,
        InlineContinuationException marker)
    {
        public IValueTaskSource<RpcFrame> Source { get; } = source;
        public short Token { get; } = token;
        public InlineContinuationException Marker { get; } = marker;
        public ValueTaskSourceStatus Status { get; set; }
        public bool Ran { get; set; }
        public bool WasPooledDuringContinuation { get; set; }
    }

    private sealed class InlineExceptionContext<TTag>(
        IValueTaskSource<RpcFrame> source,
        short token)
    {
        public IValueTaskSource<RpcFrame> Source { get; } = source;
        public short Token { get; } = token;
        public ValueTaskSourceStatus Status { get; set; }
        public Exception? Error { get; set; }
        public bool WasPooledDuringContinuation { get; set; }
    }

    private sealed class InlineContinuationException : Exception;
    private sealed class SuccessStatusTag;
    private sealed class FaultStatusTag;
    private sealed class CancellationStatusTag;
    private sealed class TimeoutStatusTag;
    private sealed class InlineThrowTag;
    private sealed class InlineFaultTag;
    private sealed class InlineCancellationTag;
}
