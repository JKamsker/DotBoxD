using System.Threading.Tasks.Sources;
using DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout.SendPhaseCalls;

namespace DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout;

/// <summary>
/// A request send that completes between the invoker's synchronous-completion check and its choice of
/// wait still runs under the finite RequestTimeout: the call fails with the timeout rather than waiting
/// forever for a response the remote never sends.
/// </summary>
public sealed class SendCompletedAfterCheckTests
{
    public static TheoryData<SendPhaseCall, bool> Calls()
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
    [MemberData(nameof(Calls))]
    public async Task Send_completing_after_the_synchronous_check_still_times_out(
        SendPhaseCall call,
        bool cancellableCaller)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(RequestTimeout)
        {
            FrameSendResult = static () => new CompletesAfterFirstStatusCheck().AsValueTask(),
        };
        using var cancellation = new CancellationTokenSource();

        var invocation = Invoke(
            harness,
            call,
            cancellableCaller ? cancellation.Token : CancellationToken.None);

        await AssertTimedOutAsync(invocation);

        // The request was written before the call timed out, so the remote is told to stop.
        Assert.Equal(harness.LastRequestMessageId, await harness.WaitForCancelAsync(Guard));
    }

    /// <summary>
    /// Reports pending to the first status check and succeeded afterwards: the send finishes exactly
    /// between the caller's synchronous fast-path check and any later inspection.
    /// </summary>
    private sealed class CompletesAfterFirstStatusCheck : IValueTaskSource
    {
        private int _statusChecks;

        public ValueTask AsValueTask() => new(this, token: 0);

        public ValueTaskSourceStatus GetStatus(short token) =>
            Interlocked.Increment(ref _statusChecks) == 1
                ? ValueTaskSourceStatus.Pending
                : ValueTaskSourceStatus.Succeeded;

        public void GetResult(short token)
        {
        }

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) =>
            ThreadPool.UnsafeQueueUserWorkItem(continuation, state, preferLocal: false);
    }
}
