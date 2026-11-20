using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout.SendPhaseCalls;

namespace DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout;

/// <summary>
/// A request that was fully written and then ends by timeout owes the remote exactly one cancel frame,
/// even when the caller cancels at the same moment and the call reports the caller's cancellation.
/// The timeout is fired by hand under a long RequestTimeout so its race with the caller is exact.
/// </summary>
public sealed class TimeoutRacingCallerCancellationTests
{
    private static readonly TimeSpan LongRequestTimeout = TimeSpan.FromSeconds(30);

    public static TheoryData<SendPhaseCall> Calls()
    {
        var data = new TheoryData<SendPhaseCall>();
        foreach (var call in AllCalls)
        {
            data.Add(call);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Timeout_and_caller_cancellation_during_inline_write_send_one_cancel_frame(SendPhaseCall call)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(LongRequestTimeout, maxPendingRequests: 1);
        using var cancellation = new CancellationTokenSource();
        harness.FrameSendResult = () =>
        {
            // The timeout claims the pending and the caller cancels while the write completes inline.
            TimeOutLastRequest(harness);
            cancellation.Cancel();
            return default;
        };

        var invocation = Invoke(harness, call, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        harness.FrameSendResult = null;

        await AssertEndedByTimeoutOrCallerAsync(invocation);
        await AssertSingleCancelFrameAsync(harness, call, messageId);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Timeout_and_caller_cancellation_as_blocked_write_finishes_send_one_cancel_frame(
        SendPhaseCall call)
    {
        await using var harness = CreateBlockedHarness(
            useFrameSender: true,
            maxPendingRequests: 1,
            requestTimeout: LongRequestTimeout);
        using var cancellation = new CancellationTokenSource();

        var invocation = Invoke(harness, call, cancellation.Token);
        var messageId = harness.LastRequestMessageId;

        // Token callbacks run newest first: the timeout claims the pending and the write completes just
        // before the call observes the caller's cancellation.
        using var race = cancellation.Token.Register(() =>
        {
            TimeOutLastRequest(harness);
            harness.ReleaseRequestSend();
        });
        cancellation.Cancel();

        await AssertEndedByTimeoutOrCallerAsync(invocation);
        await AssertSingleCancelFrameAsync(harness, call, messageId);
    }

    private static void TimeOutLastRequest(ValueTaskTimeoutTestHarness harness) =>
        Assert.True(harness.TryCancelPending(
            harness.LastRequestMessageId,
            harness.LastSentPendingResponse,
            PendingCancellationKind.Timeout));

    private static async Task AssertEndedByTimeoutOrCallerAsync(Task invocation)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => invocation.WaitAsync(Guard));
        Assert.True(
            error is OperationCanceledException or ServiceTimeoutException,
            $"Unexpected outcome: {error}");
    }

    private static async Task AssertSingleCancelFrameAsync(
        ValueTaskTimeoutTestHarness harness,
        SendPhaseCall call,
        int messageId)
    {
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));
        await InvokeAndCompleteWhenAdmittedAsync(harness, call);
        Assert.Equal(1, harness.CancelCount);
    }
}
