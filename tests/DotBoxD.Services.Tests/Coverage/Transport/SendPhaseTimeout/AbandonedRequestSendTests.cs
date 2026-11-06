using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout.SendPhaseCalls;

namespace DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout;

/// <summary>
/// A call that timed out while its send was in flight leaves the send running: the send still owns
/// the frame, keeps the call's admission slot until it finishes, and is followed by a cancel frame if
/// it eventually writes the request.
/// </summary>
public sealed class AbandonedRequestSendTests
{
    private const string LateSendFailure = "Synthetic abandoned request send failure.";

    public static TheoryData<SendPhaseCall, bool> Calls()
    {
        var data = new TheoryData<SendPhaseCall, bool>();
        foreach (var call in AllCalls)
        {
            data.Add(call, true);
            data.Add(call, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Late_send_completion_sends_cancel_and_then_releases_slot(
        SendPhaseCall call,
        bool useFrameSender)
    {
        await using var harness = CreateBlockedHarness(useFrameSender, maxPendingRequests: 1);

        var invocation = Invoke(harness, call);
        var messageId = harness.LastRequestMessageId;
        await AssertTimedOutAsync(invocation);

        // The timed-out call's send is still in flight, so its slot stays held: a new call must not
        // queue another frame behind the stuck one.
        await AssertPendingLimitAsync(Invoke(harness, call));
        Assert.Equal(0, harness.CancelCount);

        harness.ReleaseRequestSend();
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(1, harness.CancelCount);

        await InvokeAndCompleteWhenAdmittedAsync(harness, call);
        Assert.Equal(1, harness.CancelCount);
    }

    [Theory]
    [InlineData(SendPhaseCall.TaskUnary, true)]
    [InlineData(SendPhaseCall.TaskUnary, false)]
    [InlineData(SendPhaseCall.PooledUnary, true)]
    [InlineData(SendPhaseCall.PooledUnary, false)]
    [InlineData(SendPhaseCall.StreamingRequest, true)]
    public async Task Failed_late_send_releases_slot_without_cancel_frame_and_is_observed(
        SendPhaseCall call,
        bool useFrameSender)
    {
        var unobserved = new ConcurrentQueue<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args.Exception.Flatten().InnerExceptions.Any(error => error.Message == LateSendFailure))
            {
                unobserved.Enqueue(args.Exception);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            await RunFailedLateSendAsync(call, useFrameSender);
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task RunFailedLateSendAsync(SendPhaseCall call, bool useFrameSender)
    {
        await using var harness = CreateBlockedHarness(useFrameSender, maxPendingRequests: 1);

        var invocation = Invoke(harness, call);
        await AssertTimedOutAsync(invocation);
        await AssertPendingLimitAsync(Invoke(harness, call));

        harness.FailBlockedRequestSends(new InvalidOperationException(LateSendFailure));

        // Once the slot is free, a follow-up is admitted and fails on the same failed send seam.
        var deadline = DateTime.UtcNow + Guard;
        while (true)
        {
            var followUp = Invoke(harness, call);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => followUp.WaitAsync(Guard));
            if (error is InvalidOperationException { Message: LateSendFailure })
            {
                break;
            }

            Assert.Equal("Maximum pending requests reached.", error.Message);
            Assert.True(DateTime.UtcNow < deadline, "The failed send never released its slot.");
            await Task.Delay(5);
        }

        Assert.Equal(0, harness.CancelCount);
    }
}
