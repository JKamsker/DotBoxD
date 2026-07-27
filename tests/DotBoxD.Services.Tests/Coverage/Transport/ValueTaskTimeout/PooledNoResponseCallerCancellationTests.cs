using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class PooledNoResponseCallerCancellationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pre_canceled_call_does_not_send_or_reserve(bool useFrameSender)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            useFrameSender: useFrameSender);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Invoke(harness, request: 17, cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, harness.LastRequestMessageId);
        Assert.Equal(0, harness.CancelCount);

        var followUp = Invoke(harness, request: 23, CancellationToken.None);
        harness.CompleteNoResponse(harness.LastRequestMessageId);
        await followUp;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Live_token_uses_pooled_source_and_sends_one_cancel(
        bool finite,
        bool useFrameSender)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            finite ? TimeSpan.FromSeconds(30) : Timeout.InfiniteTimeSpan,
            useFrameSender: useFrameSender);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, request: 17, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        var source = harness.GetPendingResponse(messageId);
        Assert.IsType<PendingValueTaskNoResponse>(source);

        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => call.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(1, harness.CancelCount);

        await AssertSourceReusedAsync(harness, source);
    }

    [Fact]
    public async Task Option_off_keeps_live_token_task_backed()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            TimeSpan.FromSeconds(30),
            enableLowAllocation: false);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, request: 17, cancellation.Token);
        var pending = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteNoResponse(harness.LastRequestMessageId);

        await call;
        Assert.IsType<PendingReceivedResponse>(pending);
        Assert.Equal(0, harness.CancelCount);
    }

    [Fact]
    public async Task Default_token_keeps_no_response_pooled()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(TimeSpan.FromSeconds(30));

        var call = Invoke(harness, request: 17, CancellationToken.None);
        var pending = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteNoResponse(harness.LastRequestMessageId);

        await call;
        Assert.IsType<PendingValueTaskNoResponse>(pending);
        Assert.Equal(0, harness.CancelCount);
    }

    [Fact]
    public async Task Timeout_winner_recycles_and_late_caller_cannot_send_again()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(TimeSpan.FromMilliseconds(80));
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, request: 17, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        var source = harness.GetPendingResponse(messageId);

        await Assert.ThrowsAsync<ServiceTimeoutException>(() => call.AsTask().WaitAsync(Guard));
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(1, harness.CancelCount);

        cancellation.Cancel();
        Assert.Equal(1, harness.CancelCount);
        await AssertSourceReusedAsync(harness, source);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_registration_cannot_cancel_reused_source(bool useFrameSender)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            useFrameSender: useFrameSender);
        using var oldCancellation = new CancellationTokenSource();

        var first = Invoke(harness, request: 17, oldCancellation.Token);
        var source = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteNoResponse(harness.LastRequestMessageId);
        await first;

        using var currentCancellation = new CancellationTokenSource();
        var current = Invoke(harness, request: 23, currentCancellation.Token);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));

        oldCancellation.Cancel();
        Assert.False(current.IsCompleted);
        Assert.Equal(0, harness.CancelCount);
        harness.CompleteNoResponse(harness.LastRequestMessageId);
        await current;
    }

    [Fact]
    public async Task Caller_wins_response_race_while_send_is_pending()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            maxPendingRequests: 1,
            blockRequestSend: true);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, request: 17, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        var source = harness.GetPendingResponse(messageId);
        cancellation.Cancel();
        harness.CompleteNoResponse(messageId);

        Assert.False(call.IsCompleted);
        await Assert.ThrowsAsync<ServiceException>(
            () => Invoke(harness, request: 23, CancellationToken.None).AsTask());

        harness.ReleaseRequestSend();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => call.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(1, harness.CancelCount);
        await AssertSourceReusedAsync(harness, source);
    }

    [Fact]
    public async Task Response_winner_survives_later_cancellation_during_send()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            blockRequestSend: true);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, request: 17, cancellation.Token);
        harness.CompleteNoResponse(harness.LastRequestMessageId);
        cancellation.Cancel();
        harness.ReleaseRequestSend();

        await call;
        Assert.Equal(0, harness.CancelCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Send_observing_caller_cancellation_recycles_without_cancel_frame(
        bool useFrameSender)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            blockRequestSend: true,
            observeBlockedSendCancellation: true,
            useFrameSender: useFrameSender);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_response_and_callback_do_not_escape_reuse(bool useFrameSender)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            useFrameSender: useFrameSender);
        IPendingResponse? reused = null;
        for (var iteration = 0; iteration < 64; iteration++)
        {
            using var cancellation = new CancellationTokenSource();
            var call = Invoke(harness, request: 17, cancellation.Token);
            var messageId = harness.LastRequestMessageId;
            var source = harness.GetPendingResponse(messageId);
            reused ??= source;
            Assert.Same(reused, source);

            await Task.WhenAll(
                Task.Run(cancellation.Cancel),
                Task.Run(() => harness.TryCompleteNoResponse(messageId)));
            try
            {
                await call;
            }
            catch (OperationCanceledException error)
            {
                Assert.Equal(cancellation.Token, error.CancellationToken);
            }

            await AssertSourceReusedAsync(harness, reused);
        }
    }

    private static ValueTask Invoke(
        ValueTaskTimeoutTestHarness harness,
        int request,
        CancellationToken cancellationToken) =>
        harness.Invoker.InvokeValueAsync<int>(
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
        harness.CompleteNoResponse(harness.LastRequestMessageId);
        await followUp;
    }
}
