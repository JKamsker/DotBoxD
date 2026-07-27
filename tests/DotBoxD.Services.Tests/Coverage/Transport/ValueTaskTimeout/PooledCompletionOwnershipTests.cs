using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class PooledCompletionOwnershipTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Success_before_direct_owner_publication_releases_slot()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan)
        {
            ReentrantResponse = ReentrantResponseKind.Success,
        };

        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeGeneric(harness, request: 1));
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeGeneric(harness, request: 2));
    }

    [Fact]
    public async Task Error_before_direct_owner_publication_releases_slot()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan)
        {
            ReentrantResponse = ReentrantResponseKind.Error,
        };

        await Assert.ThrowsAsync<RemoteServiceException>(
            () => InvokeGeneric(harness, request: 1).AsTask());

        harness.ReentrantResponse = ReentrantResponseKind.Success;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeGeneric(harness, request: 2));
    }

    [Fact]
    public async Task No_response_error_before_direct_owner_publication_releases_slot()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan)
        {
            ReentrantResponse = ReentrantResponseKind.Error,
        };

        await Assert.ThrowsAsync<RemoteServiceException>(
            () => InvokeNoResponse(harness, request: 1).AsTask());

        harness.ReentrantResponse = ReentrantResponseKind.None;
        var followUp = InvokeNoResponse(harness, request: 2);
        harness.CompleteNoResponse(harness.LastRequestMessageId);
        await followUp;
    }

    [Fact]
    public async Task Pending_send_does_not_start_finite_timeout_until_send_completes()
    {
        var requestTimeout = TimeSpan.FromMilliseconds(80);
        await using var harness = new ValueTaskTimeoutTestHarness(
            requestTimeout,
            blockRequestSend: true);

        var call = InvokeGeneric(harness, request: 1);
        var messageId = harness.LastRequestMessageId;
        var pending = harness.GetPendingResponse(messageId);
        var task = call.AsTask();

        var premature = await Task.WhenAny(task, Task.Delay(requestTimeout * 3));
        Assert.NotSame(task, premature);

        harness.ReleaseRequestSend();
        var error = await Assert.ThrowsAsync<ServiceTimeoutException>(
            () => task.WaitAsync(TestTimeout));
        Assert.Contains(
            $"{ValueTaskTimeoutTestHarness.Service}.{ValueTaskTimeoutTestHarness.Method}",
            error.Message);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(TestTimeout));
        Assert.IsType<PendingValueTaskUnaryResponse<int>>(pending);
    }

    [Fact]
    public async Task No_response_pending_send_starts_finite_timeout_after_send_completes()
    {
        var requestTimeout = TimeSpan.FromMilliseconds(80);
        await using var harness = new ValueTaskTimeoutTestHarness(
            requestTimeout,
            blockRequestSend: true);

        var call = InvokeNoResponse(harness, request: 1);
        var messageId = harness.LastRequestMessageId;
        var pending = harness.GetPendingResponse(messageId);
        var task = call.AsTask();

        var premature = await Task.WhenAny(task, Task.Delay(requestTimeout * 3));
        Assert.NotSame(task, premature);

        harness.ReleaseRequestSend();
        var error = await Assert.ThrowsAsync<ServiceTimeoutException>(
            () => task.WaitAsync(TestTimeout));
        Assert.Contains(
            $"{ValueTaskTimeoutTestHarness.Service}.{ValueTaskTimeoutTestHarness.Method}",
            error.Message);
        Assert.False(task.IsCanceled);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(TestTimeout));
        Assert.Equal(1, harness.CancelCount);
        Assert.IsType<PendingValueTaskNoResponse>(pending);
    }

    [Fact]
    public async Task Reentrant_response_winning_a_failed_send_recycles_unissued_source()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan)
        {
            FailRequestSend = true,
            ReentrantResponse = ReentrantResponseKind.Success,
        };

        var failed = InvokeGeneric(harness, request: 1);
        var firstSource = harness.LastSentPendingResponse;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => failed.AsTask());
        Assert.Equal(ValueTaskTimeoutTestHarness.SendFailureMessage, error.Message);

        harness.FailRequestSend = false;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeGeneric(harness, request: 2));
        Assert.Same(firstSource, harness.LastSentPendingResponse);
    }

    private static ValueTask<int> InvokeGeneric(
        ValueTaskTimeoutTestHarness harness,
        int request) =>
        harness.Invoker.InvokeValueAsync<int, int>(
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
}
