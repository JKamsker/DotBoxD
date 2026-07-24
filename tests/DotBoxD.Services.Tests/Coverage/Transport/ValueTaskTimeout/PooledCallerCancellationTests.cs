using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Serialization;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class PooledCallerCancellationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_token_uses_pooled_generic_source_and_preserves_original_token(bool finite)
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            finite ? TimeSpan.FromSeconds(30) : Timeout.InfiniteTimeSpan);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, CallerResponse.First, cancellation.Token);
        var source = harness.GetPendingResponse(harness.LastRequestMessageId);
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => call.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.IsType<PendingValueTaskUnaryResponse<CallerResponse>>(source);
        Assert.Equal(harness.LastRequestMessageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(1, harness.CancelCount);

        using var followUpCancellation = new CancellationTokenSource();
        var followUp = Invoke(harness, CallerResponse.Second, followUpCancellation.Token);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.Second);
        Assert.Equal(CallerResponse.Second, await followUp);
    }

    [Fact]
    public async Task Disabled_low_allocation_option_keeps_live_token_task_backed()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            enableLowAllocation: false);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, CallerResponse.First, cancellation.Token);
        var pending = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.First);

        Assert.Equal(CallerResponse.First, await call);
        Assert.IsType<CancellablePendingUnaryResponse<CallerResponse>>(pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_response_materialization_wins_after_correlation_removal(bool useFrameSender)
    {
        using var cancellation = new CancellationTokenSource();
        var serializer = new CancelingResponseSerializer(cancellation);
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            serializer: serializer, useFrameSender: useFrameSender);

        var call = Invoke(harness, CallerResponse.First, cancellation.Token);
        var source = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.First);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => call.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.IsType<PendingValueTaskUnaryResponse<CallerResponse>>(source);
        Assert.Equal(harness.LastRequestMessageId, await harness.WaitForCancelAsync(Guard));
        Assert.Equal(1, harness.CancelCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Serializer_timeout_exception_does_not_send_request_cancel(bool useFrameSender)
    {
        var expected = new ServiceTimeoutException("Synthetic serializer timeout.");
        var serializer = new ThrowingResponseSerializer(expected);
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            serializer: serializer,
            useFrameSender: useFrameSender);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, CallerResponse.First, cancellation.Token);
        var source = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.First);

        var error = await Assert.ThrowsAsync<ServiceTimeoutException>(() => call.AsTask());
        Assert.Same(expected, error);
        Assert.Equal(0, harness.CancelCount);

        var followUp = Invoke(harness, CallerResponse.Second, CancellationToken.None);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.Second);
        Assert.Equal(CallerResponse.Second, await followUp);
    }

    [Fact]
    public async Task Disposed_old_registration_cannot_cancel_reused_generation()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan);
        using var oldCancellation = new CancellationTokenSource();

        var first = Invoke(harness, CallerResponse.First, oldCancellation.Token);
        var source = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.First);
        Assert.Equal(CallerResponse.First, await first);

        using var currentCancellation = new CancellationTokenSource();
        var current = Invoke(harness, CallerResponse.Second, currentCancellation.Token);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));

        oldCancellation.Cancel();
        Assert.False(current.IsCompleted);
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.Second);

        Assert.Equal(CallerResponse.Second, await current);
        Assert.Equal(0, harness.CancelCount);
    }

    [Fact]
    public async Task Timeout_winner_is_not_overwritten_by_late_caller_cancellation()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(TimeSpan.FromMilliseconds(80));
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, CallerResponse.First, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        var source = harness.GetPendingResponse(messageId);

        await Assert.ThrowsAsync<ServiceTimeoutException>(() => call.AsTask().WaitAsync(Guard));
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));

        cancellation.Cancel();
        Assert.Equal(1, harness.CancelCount);

        var followUp = Invoke(harness, CallerResponse.Second, CancellationToken.None);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.Second);
        Assert.Equal(CallerResponse.Second, await followUp);
    }

    [Fact]
    public async Task Caller_cancellation_before_response_waits_for_send_then_releases_slot()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            maxPendingRequests: 1,
            blockRequestSend: true);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, CallerResponse.First, cancellation.Token);
        var messageId = harness.LastRequestMessageId;
        var source = harness.GetPendingResponse(messageId);
        cancellation.Cancel();
        harness.CompleteGeneric(messageId, CallerResponse.First);

        Assert.False(call.IsCompleted);
        await Assert.ThrowsAsync<ServiceException>(
            () => Invoke(harness, CallerResponse.Second, CancellationToken.None).AsTask());

        harness.ReleaseRequestSend();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => call.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(Guard));

        var followUp = Invoke(harness, CallerResponse.Second, CancellationToken.None);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.Second);
        Assert.Equal(CallerResponse.Second, await followUp);
    }

    [Fact]
    public async Task Response_before_cancellation_during_send_retains_response_winner()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            blockRequestSend: true);
        using var cancellation = new CancellationTokenSource();

        var call = Invoke(harness, CallerResponse.First, cancellation.Token);
        harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.First);
        cancellation.Cancel();
        harness.ReleaseRequestSend();

        Assert.Equal(CallerResponse.First, await call);
        Assert.Equal(0, harness.CancelCount);
    }

    [Fact]
    public async Task Concurrent_response_and_callback_do_not_corrupt_follow_up_generation()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan);
        IPendingResponse? reused = null;
        for (var iteration = 0; iteration < 64; iteration++)
        {
            using var cancellation = new CancellationTokenSource();
            var call = Invoke(harness, CallerResponse.First, cancellation.Token);
            var messageId = harness.LastRequestMessageId;
            var source = harness.GetPendingResponse(messageId);
            reused ??= source;
            Assert.Same(reused, source);

            await Task.WhenAll(
                Task.Run(cancellation.Cancel),
                Task.Run(() => harness.TryCompleteGeneric(messageId, CallerResponse.First)));
            try
            {
                _ = await call;
            }
            catch (OperationCanceledException error)
            {
                Assert.Equal(cancellation.Token, error.CancellationToken);
            }

            var followUp = Invoke(harness, CallerResponse.Second, CancellationToken.None);
            Assert.Same(reused, harness.GetPendingResponse(harness.LastRequestMessageId));
            harness.CompleteGeneric(harness.LastRequestMessageId, CallerResponse.Second);
            Assert.Equal(CallerResponse.Second, await followUp);
        }
    }

    private static ValueTask<CallerResponse> Invoke(
        ValueTaskTimeoutTestHarness harness,
        CallerResponse request,
        CancellationToken cancellationToken) =>
        harness.Invoker.InvokeValueAsync<CallerResponse, CallerResponse>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request,
            cancellationToken);

    private enum CallerResponse
    {
        First = 17,
        Second = 23,
    }

    private sealed class CancelingResponseSerializer(CancellationTokenSource cancellation) : ISerializer
    {
        private readonly MessagePackRpcSerializer _inner = new();

        public void Serialize<T>(IBufferWriter<byte> writer, T value) =>
            _inner.Serialize(writer, value);

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            if (typeof(T) == typeof(CallerResponse))
            {
                cancellation.Cancel();
            }

            return _inner.Deserialize<T>(data);
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
        {
            if (type == typeof(CallerResponse))
            {
                cancellation.Cancel();
            }

            return _inner.Deserialize(data, type);
        }
    }

    private sealed class ThrowingResponseSerializer(ServiceTimeoutException error) : ISerializer
    {
        private readonly MessagePackRpcSerializer _inner = new();
        private int _throwNextResponse = 1;

        public void Serialize<T>(IBufferWriter<byte> writer, T value) =>
            _inner.Serialize(writer, value);

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            if (typeof(T) == typeof(CallerResponse) &&
                Interlocked.Exchange(ref _throwNextResponse, 0) != 0)
            {
                throw error;
            }

            return _inner.Deserialize<T>(data);
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
            _inner.Deserialize(data, type);
    }
}
