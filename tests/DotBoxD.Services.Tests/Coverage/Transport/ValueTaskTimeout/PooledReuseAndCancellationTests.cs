using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class PooledReuseAndCancellationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Live_caller_token_uses_generic_pooled_source()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan);
        using var cancellation = new CancellationTokenSource();

        var call = harness.Invoker.InvokeValueAsync<int, int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 1,
            cancellation.Token);
        var pending = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteGeneric(harness.LastRequestMessageId);

        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await call);
        Assert.IsType<PendingValueTaskUnaryResponse<int>>(pending);
    }

    [Fact]
    public async Task Live_caller_token_keeps_no_response_invocation_task_backed()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan);
        using var cancellation = new CancellationTokenSource();

        var call = harness.Invoker.InvokeValueAsync<int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 1,
            cancellation.Token);
        var pending = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteNoResponse(harness.LastRequestMessageId);

        await call;
        Assert.IsType<PendingReceivedResponse>(pending);
    }

    [Fact]
    public async Task Same_generic_source_is_reused_after_timeout_error_and_success()
    {
        var requestTimeout = TimeSpan.FromMilliseconds(80);
        await using var harness = new ValueTaskTimeoutTestHarness(requestTimeout);

        var timedOut = InvokeReuse(harness, request: 1);
        var source = harness.GetPendingResponse(harness.LastRequestMessageId);
        await Assert.ThrowsAsync<ServiceTimeoutException>(
            () => timedOut.AsTask().WaitAsync(TestTimeout));

        var remoteError = InvokeReuse(harness, request: 2);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteError(harness.LastRequestMessageId);
        await Assert.ThrowsAsync<RemoteServiceException>(() => remoteError.AsTask());

        var success = InvokeReuse(harness, request: 3);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteGeneric(harness.LastRequestMessageId, ReuseResponse.Success);
        Assert.Equal(ReuseResponse.Success, await success);

        var afterSuccess = InvokeReuse(harness, request: 4);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteGeneric(harness.LastRequestMessageId, ReuseResponse.Success);
        Assert.Equal(ReuseResponse.Success, await afterSuccess);
    }

    [Fact]
    public async Task Same_no_response_source_is_reused_after_timeout_error_and_success()
    {
        var requestTimeout = TimeSpan.FromMilliseconds(80);
        await using var harness = new ValueTaskTimeoutTestHarness(requestTimeout);

        var timedOut = InvokeNoResponse(harness, request: 1);
        var source = harness.GetPendingResponse(harness.LastRequestMessageId);
        await Assert.ThrowsAsync<ServiceTimeoutException>(
            () => timedOut.AsTask().WaitAsync(TestTimeout));

        var remoteError = InvokeNoResponse(harness, request: 2);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteError(harness.LastRequestMessageId);
        await Assert.ThrowsAsync<RemoteServiceException>(() => remoteError.AsTask());

        var success = InvokeNoResponse(harness, request: 3);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteNoResponse(harness.LastRequestMessageId);
        await success;

        var afterSuccess = InvokeNoResponse(harness, request: 4);
        Assert.Same(source, harness.GetPendingResponse(harness.LastRequestMessageId));
        harness.CompleteNoResponse(harness.LastRequestMessageId);
        await afterSuccess;
    }

    [Fact]
    public async Task Concurrent_generic_sources_are_distinct_and_reused()
    {
        const int concurrentCalls = 32;
        await using var harness = new ValueTaskTimeoutTestHarness(
            Timeout.InfiniteTimeSpan,
            maxPendingRequests: concurrentCalls);

        var first = ReserveConcurrentWave(harness, concurrentCalls);
        Assert.Equal(concurrentCalls, first.Sources.Count);

        Parallel.ForEach(
            first.MessageIds,
            messageId => harness.CompleteGeneric(messageId, ConcurrentResponse.Success));
        await Task.WhenAll(first.Calls.Select(call => call.AsTask()));

        var second = ReserveConcurrentWave(harness, concurrentCalls);
        Assert.Equal(concurrentCalls, second.Sources.Count);
        Assert.True(first.Sources.SetEquals(second.Sources));

        Parallel.ForEach(
            second.MessageIds,
            messageId => harness.CompleteGeneric(messageId, ConcurrentResponse.Success));
        await Task.WhenAll(second.Calls.Select(call => call.AsTask()));
    }

    private static ConcurrentWave ReserveConcurrentWave(
        ValueTaskTimeoutTestHarness harness,
        int count)
    {
        var calls = new ValueTask<ConcurrentResponse>[count];
        var messageIds = new int[count];
        var sources = new HashSet<IPendingResponse>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < count; i++)
        {
            calls[i] = harness.Invoker.InvokeValueAsync<int, ConcurrentResponse>(
                ValueTaskTimeoutTestHarness.Service,
                ValueTaskTimeoutTestHarness.Method,
                i);
            messageIds[i] = harness.LastRequestMessageId;
            sources.Add(harness.GetPendingResponse(messageIds[i]));
        }

        return new ConcurrentWave(calls, messageIds, sources);
    }

    private static ValueTask<ReuseResponse> InvokeReuse(
        ValueTaskTimeoutTestHarness harness,
        int request) =>
        harness.Invoker.InvokeValueAsync<int, ReuseResponse>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request);

    private static ValueTask InvokeNoResponse(
        ValueTaskTimeoutTestHarness harness,
        int request) =>
        harness.Invoker.InvokeValueAsync<int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request);

    private enum ReuseResponse
    {
        Success = 1,
    }

    private enum ConcurrentResponse
    {
        Success = 1,
    }

    private sealed record ConcurrentWave(
        ValueTask<ConcurrentResponse>[] Calls,
        int[] MessageIds,
        HashSet<IPendingResponse> Sources);
}
