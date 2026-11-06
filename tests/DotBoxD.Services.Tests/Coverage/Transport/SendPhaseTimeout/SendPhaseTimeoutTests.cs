using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout.SendPhaseCalls;

namespace DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout;

/// <summary>
/// A finite RequestTimeout bounds the whole call: a request send that never completes fails with the
/// same timeout as a late response, on every outbound call shape and both send seams.
/// </summary>
public sealed class SendPhaseTimeoutTests
{
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

    [Theory]
    [MemberData(nameof(CallMatrix))]
    public async Task Send_that_never_completes_fails_with_request_timeout(
        SendPhaseCall call,
        bool useFrameSender,
        bool cancellableCaller)
    {
        await using var harness = CreateBlockedHarness(useFrameSender);
        using var cancellation = new CancellationTokenSource();

        var invocation = Invoke(
            harness,
            call,
            cancellableCaller ? cancellation.Token : CancellationToken.None);

        await AssertTimedOutAsync(invocation);
        // The request has not been written yet, so there is nothing for the remote to cancel.
        Assert.Equal(0, harness.CancelCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Streaming_request_send_timeout_disposes_unstarted_stream_source(bool useFrameSender)
    {
        await using var harness = CreateBlockedHarness(useFrameSender);
        var source = new TrackingSourceStream();

        var invocation = InvokeStreaming(harness, source);
        var messageId = harness.LastRequestMessageId;

        await AssertTimedOutAsync(invocation);
        await source.Disposed.WaitAsync(Guard);
        Assert.Equal(0, source.ReadCount);

        // The late send still reaches the remote, which is then told to stop; the abandoned
        // request never starts pumping its stream.
        harness.ReleaseRequestSend();
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(0, source.ReadCount);
    }

    [Theory]
    [InlineData(SendPhaseCall.TaskUnary)]
    [InlineData(SendPhaseCall.PooledUnary)]
    [InlineData(SendPhaseCall.PooledNoResponse)]
    [InlineData(SendPhaseCall.TaskNoResponse)]
    public async Task Response_during_pending_send_waits_for_send_only_until_deadline(SendPhaseCall call)
    {
        var requestTimeout = TimeSpan.FromMilliseconds(400);
        await using var harness = CreateBlockedHarness(useFrameSender: true, requestTimeout: requestTimeout);

        var invocation = Invoke(harness, call);
        CompleteLastRequest(harness, call);

        // As without a timeout, a response does not end the call while its send is in flight...
        Assert.NotSame(invocation, await Task.WhenAny(invocation, Task.Delay(requestTimeout / 8)));

        // ...but a send still stuck at the deadline no longer holds the response back, while the
        // detached send keeps the admission slot.
        await invocation.WaitAsync(Guard);
        await AssertPendingLimitAsync(Invoke(harness, call));

        harness.ReleaseRequestSend();
        await InvokeAndCompleteWhenAdmittedAsync(harness, call);
        Assert.Equal(0, harness.CancelCount);
    }

    [Fact]
    public async Task Caller_cancellation_observed_by_pending_send_still_wins()
    {
        await using var harness = new ValueTaskTimeout.ValueTaskTimeoutTestHarness(
            TimeSpan.FromSeconds(30),
            blockRequestSend: true,
            observeBlockedSendCancellation: true);
        using var cancellation = new CancellationTokenSource();

        var invocation = Invoke(harness, SendPhaseCall.PooledUnary, cancellation.Token);
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invocation.WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, harness.CancelCount);
    }
}
