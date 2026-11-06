using DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout.SendPhaseCalls;

namespace DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout;

/// <summary>
/// Frame writes are atomic, so a request write that already started ignores the caller's token. Caller
/// cancellation still ends the call promptly with the caller's OperationCanceledException, with or
/// without a finite RequestTimeout. The unfinished send is detached like a timed-out one: it keeps the
/// admission slot until it finishes and is followed by a cancel frame if it writes the request late.
/// </summary>
public sealed class SendPhaseCallerCancellationTests
{
    // Long enough that only caller cancellation can end the call within the test guard.
    private static readonly TimeSpan LongRequestTimeout = TimeSpan.FromSeconds(30);

    public static TheoryData<SendPhaseCall, bool, bool> CallMatrix()
    {
        var data = new TheoryData<SendPhaseCall, bool, bool>();
        foreach (var call in AllCalls)
        {
            data.Add(call, true, false);
            data.Add(call, true, true);
            data.Add(call, false, false);
            data.Add(call, false, true);
        }

        return data;
    }

    public static TheoryData<SendPhaseCall, bool> TimeoutMatrix()
    {
        var data = new TheoryData<SendPhaseCall, bool>();
        foreach (var call in AllCalls)
        {
            data.Add(call, false);
            data.Add(call, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CallMatrix))]
    public async Task Caller_cancellation_ends_call_whose_started_write_never_completes(
        SendPhaseCall call,
        bool useFrameSender,
        bool finiteTimeout)
    {
        await using var harness = CreateBlockedHarness(
            useFrameSender,
            maxPendingRequests: 1,
            requestTimeout: GetTimeout(finiteTimeout));
        using var cancellation = new CancellationTokenSource();

        var invocation = Invoke(harness, call, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        Assert.False(invocation.IsCompleted);

        cancellation.Cancel();

        await AssertCallerCanceledAsync(invocation, cancellation.Token);
        Assert.Equal(0, harness.CancelCount);

        // The write is still in flight, so its slot stays held: no new frame queues behind it.
        await AssertPendingLimitAsync(Invoke(harness, call));

        // The request reaches the remote after its caller gave up; the remote is told to stop.
        harness.ReleaseRequestSend();
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));

        await InvokeAndCompleteWhenAdmittedAsync(harness, call);
        Assert.Equal(1, harness.CancelCount);
    }

    [Theory]
    [MemberData(nameof(CallMatrix))]
    public async Task Send_that_observes_caller_cancellation_yields_caller_cancellation_without_cancel_frame(
        SendPhaseCall call,
        bool useFrameSender,
        bool finiteTimeout)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            GetTimeout(finiteTimeout),
            maxPendingRequests: 1,
            blockRequestSend: true,
            observeBlockedSendCancellation: true,
            useFrameSender: useFrameSender);
        using var cancellation = new CancellationTokenSource();

        var invocation = Invoke(harness, call, cancellation.Token);
        cancellation.Cancel();

        await AssertCallerCanceledAsync(invocation, cancellation.Token);

        // The send gave up before writing, so the slot comes back and nothing is cancelled remotely.
        harness.ReleaseRequestSend();
        await InvokeAndCompleteWhenAdmittedAsync(harness, call);
        Assert.Equal(0, harness.CancelCount);
    }

    [Theory]
    [MemberData(nameof(TimeoutMatrix))]
    public async Task Write_finishing_as_caller_cancels_still_sends_cancel_frame(
        SendPhaseCall call,
        bool finiteTimeout)
    {
        await using var harness = CreateBlockedHarness(
            useFrameSender: true,
            maxPendingRequests: 1,
            requestTimeout: GetTimeout(finiteTimeout));
        using var cancellation = new CancellationTokenSource();

        var invocation = Invoke(harness, call, cancellation.Token);
        var messageId = harness.LastRequestMessageId;

        // Token callbacks run newest first, so the write completes just before the call observes
        // cancellation: the call cancels a request that was fully written.
        using var finishWrite = cancellation.Token.Register(harness.ReleaseRequestSend);
        cancellation.Cancel();

        await AssertCallerCanceledAsync(invocation, cancellation.Token);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));

        await InvokeAndCompleteWhenAdmittedAsync(harness, call);
        Assert.Equal(1, harness.CancelCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Caller_cancellation_during_blocked_send_disposes_unstarted_stream_source(
        bool useFrameSender,
        bool finiteTimeout)
    {
        await using var harness = CreateBlockedHarness(
            useFrameSender,
            requestTimeout: GetTimeout(finiteTimeout));
        using var cancellation = new CancellationTokenSource();
        var source = new TrackingSourceStream();

        var invocation = InvokeStreaming(harness, source, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        cancellation.Cancel();

        await AssertCallerCanceledAsync(invocation, cancellation.Token);
        await source.Disposed.WaitAsync(Guard);

        harness.ReleaseRequestSend();
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(0, source.ReadCount);
    }

    [Theory]
    [InlineData(SendPhaseCall.TaskUnary)]
    [InlineData(SendPhaseCall.TaskNoResponse)]
    [InlineData(SendPhaseCall.PooledUnary)]
    [InlineData(SendPhaseCall.PooledNoResponse)]
    public async Task Caller_cancellation_after_response_ends_wait_for_blocked_send(SendPhaseCall call)
    {
        await using var harness = CreateBlockedHarness(
            useFrameSender: true,
            requestTimeout: Timeout.InfiniteTimeSpan);
        using var cancellation = new CancellationTokenSource();

        var invocation = Invoke(harness, call, cancellation.Token);
        CompleteLastRequest(harness, call);

        // A response does not end the call while its send is in flight; caller cancellation does.
        Assert.NotSame(invocation, await Task.WhenAny(invocation, Task.Delay(50)));
        cancellation.Cancel();

        // Each path keeps its existing winner for a response delivered before cancellation: the
        // Task unary path lets cancellation win, the others keep the delivered response.
        if (call == SendPhaseCall.TaskUnary)
        {
            await AssertCallerCanceledAsync(invocation, cancellation.Token);
        }
        else
        {
            await invocation.WaitAsync(Guard);
        }

        await AssertPendingLimitAsync(Invoke(harness, call));

        // The remote already answered, so the late write needs no cancel frame.
        harness.ReleaseRequestSend();
        await InvokeAndCompleteWhenAdmittedAsync(harness, call);
        Assert.Equal(0, harness.CancelCount);
    }

    private static TimeSpan GetTimeout(bool finiteTimeout) =>
        finiteTimeout ? LongRequestTimeout : Timeout.InfiniteTimeSpan;

    private static async Task AssertCallerCanceledAsync(Task call, CancellationToken token)
    {
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Guard));
        Assert.Equal(token, error.CancellationToken);
    }
}
