using DotBoxD.Services.Client;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class PooledCallerCancellationOrderingTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Caller_observation_overrides_timeout_that_won_during_send()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            blockRequestSend: true);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, request: 17, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        var source = harness.GetPendingResponse(messageId);
        Assert.True(harness.TryCancelPending(messageId, source, PendingCancellationKind.Timeout));

        cancellation.Cancel();
        harness.ReleaseRequestSend();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => call.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(1, harness.CancelCount);

        await AssertSourceReusedAsync(harness, source);
    }

    [Fact]
    public async Task Send_observing_caller_cancellation_releases_without_cancel_frame()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            blockRequestSend: true,
            observeBlockedSendCancellation: true);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, request: 17, cancellation.Token);
        var source = harness.GetPendingResponse(harness.LastRequestMessageId);
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => call.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, harness.CancelCount);

        harness.ReleaseRequestSend();
        await AssertSourceReusedAsync(harness, source);
    }

    private static ValueTask<int> Invoke(
        ValueTaskTimeoutTestHarness harness,
        int request,
        CancellationToken cancellationToken) =>
        harness.Invoker.InvokeValueAsync<int, int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request,
            cancellationToken);

    private static async Task AssertSourceReusedAsync(
        ValueTaskTimeoutTestHarness harness,
        IPendingResponse source)
    {
        var followUp = Invoke(harness, request: 23, CancellationToken.None);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteGeneric(harness.LastRequestMessageId, value: 29);
        Assert.Equal(29, await followUp);
    }
}
