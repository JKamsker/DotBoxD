using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.Pooling;

public sealed class PooledFrameSendOperationContinuationTests
{
    private static readonly AsyncLocal<object?> Context = new();
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public void InlineThrowingConsumer_DoesNotStrandOrPrematurelyRecycleOperation()
    {
        var operation = RequireOperation<InlineThrowTag>();
        var source = new ControlledPendingSend();
        var send = operation.Issue(source.Pending);
        var marker = new InlineConsumerException();
        var completion = new InlineThrowCompletion();
#pragma warning disable xUnit1030 // The raw continuation pins source-publication reentrancy.
        var awaiter = send.ConfigureAwait(false).GetAwaiter();
#pragma warning restore xUnit1030
        awaiter.UnsafeOnCompleted(() =>
        {
            awaiter.GetResult();
            completion.Ran = true;
            completion.WasPooledDuringContinuation =
                TestPooledFrameSendOperation<InlineThrowTag>.TryTakeRecycled() is not null;
            throw marker;
        });

        var thrown = Assert.Throws<InlineConsumerException>(source.Succeed);

        Assert.Same(marker, thrown);
        Assert.True(completion.Ran);
        Assert.False(completion.WasPooledDuringContinuation);
        Assert.Same(
            operation,
            TestPooledFrameSendOperation<InlineThrowTag>.TryTakeRecycled());
        Assert.Null(TestPooledFrameSendOperation<InlineThrowTag>.TryTakeRecycled());
    }

    [Fact]
    public async Task EverySuspension_RestoresIssuingExecutionContext()
    {
        var previous = Context.Value;
        var callerContext = new object();
        var producerContext = new object();
        Context.Value = callerContext;
        try
        {
            var operation = RequireOperation<ExecutionContextTag>();
            var firstSource = new ControlledPendingSend();
            var secondSource = new ControlledPendingSend();
            var observedContexts = new List<object?>();
            var send = operation.Issue(
                firstSource.Pending,
                resumeHandler: (current, pending) =>
                {
                    observedContexts.Add(Context.Value);
                    pending.GetAwaiter().GetResult();
                    if (observedContexts.Count == 1)
                    {
                        current.RegisterNext(secondSource.Pending);
                    }
                    else
                    {
                        current.CompleteSuccessfully();
                    }
                });

            await CompleteWithContextAsync(firstSource, producerContext);
            Assert.False(send.IsCompleted);
            await CompleteWithContextAsync(secondSource, producerContext);

            Assert.True(send.IsCompletedSuccessfully);
            await send;
            Assert.Equal(2, operation.ResumeCount);
            Assert.Equal(new[] { callerContext, callerContext }, observedContexts);
            Assert.Same(
                operation,
                TestPooledFrameSendOperation<ExecutionContextTag>.TryTakeRecycled());
        }
        finally
        {
            Context.Value = previous;
        }
    }

    [Fact]
    public async Task StaleRegistrationFailure_DoesNotClearReusedLease()
    {
        var operation = RequireOperation<RegistrationRaceTag>();
        var firstSource = new ControlledPendingSend();
        var marker = new RegistrationRaceException();
        var inlineSource = new InlineCompletionThenThrowingSend(marker);
        var resumeCount = 0;
        var firstSend = operation.Issue(
            firstSource.Pending,
            resumeHandler: (current, pending) =>
            {
                pending.GetAwaiter().GetResult();
                if (Interlocked.Increment(ref resumeCount) == 1)
                {
                    current.RegisterNext(inlineSource.Pending);
                }
                else
                {
                    current.CompleteSuccessfully();
                }
            });
        var firstConsumption = firstSend.AsTask();

        var oldProducer = Task.Run(() => Record.Exception(firstSource.Succeed));
        await inlineSource.ContinuationReturned.WaitAsync(Guard);
        await firstConsumption.WaitAsync(Guard);

        var reused = RequireOperation<RegistrationRaceTag>();
        Assert.Same(operation, reused);
        var retainedState = new object();
        var nextSource = new ControlledPendingSend();
        var nextSend = reused.Issue(nextSource.Pending, retainedState);

        inlineSource.AllowThrow();
        var oldError = await oldProducer.WaitAsync(Guard);

        Assert.Same(marker, oldError);
        Assert.Same(retainedState, reused.RetainedState);
        Assert.False(nextSend.IsCompleted);
        nextSource.Succeed();
        await nextSend;
        Assert.Equal(1, nextSource.GetResultCount);
        Assert.Same(
            reused,
            TestPooledFrameSendOperation<RegistrationRaceTag>.TryTakeRecycled());
    }

    private static Task CompleteWithContextAsync(
        ControlledPendingSend source,
        object producerContext) =>
        Task.Run(() =>
        {
            Context.Value = producerContext;
            source.Succeed();
        });

    private static TestPooledFrameSendOperation<TTag> RequireOperation<TTag>() =>
        TestPooledFrameSendOperation<TTag>.RentOrCreate()
        ?? throw new InvalidOperationException("The isolated send-operation population is exhausted.");

    private sealed class InlineThrowCompletion
    {
        public bool Ran { get; set; }

        public bool WasPooledDuringContinuation { get; set; }
    }

    private sealed class InlineThrowTag;
    private sealed class ExecutionContextTag;
    private sealed class RegistrationRaceTag;
    private sealed class InlineConsumerException : Exception;
    private sealed class RegistrationRaceException : Exception;
}
