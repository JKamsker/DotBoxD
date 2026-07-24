using DotBoxD.Services.Client;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class PooledDirectCallerCancellationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pre_canceled_call_does_not_send_or_reserve_a_request(bool useFrameSender)
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
        harness.CompleteGeneric(harness.LastRequestMessageId, value: 29);
        Assert.Equal(29, await followUp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Immediate_cancellation_disposes_registration_before_rapid_reuse(
        bool useFrameSender)
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

            cancellation.Cancel();

            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => call.AsTask().WaitAsync(Guard));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(iteration + 1, harness.CancelCount);

            var followUp = Invoke(harness, request: 23, CancellationToken.None);
            Assert.Same(reused, harness.GetPendingResponse(harness.LastRequestMessageId));
            harness.CompleteGeneric(harness.LastRequestMessageId, value: 29);
            Assert.Equal(29, await followUp);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_callback_and_consumption_do_not_deadlock_or_escape_reuse(
        bool useFrameSender)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            useFrameSender: useFrameSender);
        IPendingResponse? reused = null;

        for (var iteration = 0; iteration < 128; iteration++)
        {
            using var cancellation = new CancellationTokenSource();
            var call = Invoke(harness, request: 17, cancellation.Token);
            var source = harness.GetPendingResponse(harness.LastRequestMessageId);
            reused ??= source;
            Assert.Same(reused, source);
            var observation = call.AsTask();

            var cancellationTask = Task.Run(cancellation.Cancel);
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => observation.WaitAsync(Guard));
            await cancellationTask.WaitAsync(Guard);

            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(iteration + 1, harness.CancelCount);

            var followUp = Invoke(harness, request: 23, CancellationToken.None);
            Assert.Same(reused, harness.GetPendingResponse(harness.LastRequestMessageId));
            harness.CompleteGeneric(harness.LastRequestMessageId, value: 29);
            Assert.Equal(29, await followUp);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Response_winner_disposes_old_registration_before_live_token_reuse(
        bool useFrameSender)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            useFrameSender: useFrameSender);
        using var oldCancellation = new CancellationTokenSource();

        var first = Invoke(harness, request: 17, oldCancellation.Token);
        var source = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteGeneric(harness.LastRequestMessageId, value: 19);
        Assert.Equal(19, await first);

        using var currentCancellation = new CancellationTokenSource();
        var current = Invoke(harness, request: 23, currentCancellation.Token);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));

        oldCancellation.Cancel();
        Assert.False(current.IsCompleted);
        Assert.Equal(0, harness.CancelCount);
        harness.CompleteGeneric(harness.LastRequestMessageId, value: 29);
        Assert.Equal(29, await current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_observation_overrides_timeout_that_won_after_synchronous_send(
        bool useFrameSender)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            useFrameSender: useFrameSender);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, request: 17, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        var source = harness.GetPendingResponse(messageId);
        Assert.True(harness.TryCancelPending(
            messageId,
            source,
            PendingCancellationKind.Timeout));

        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => call.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(1, harness.CancelCount);

        var followUp = Invoke(harness, request: 23, CancellationToken.None);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteGeneric(harness.LastRequestMessageId, value: 29);
        Assert.Equal(29, await followUp);
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
}
