using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class TaskBackedDirectCompletionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Duplicate_direct_owner_publication_fails_closed()
    {
        await using var firstOwner = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan);
        await using var secondOwner = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan);
        var pending = new PendingUnaryResponse<int>(messageId: 1);

        pending.EnableDirectCompletion(firstOwner.Invoker);
        var error = Assert.Throws<InvalidOperationException>(
            () => pending.EnableDirectCompletion(secondOwner.Invoker));

        Assert.Equal("Direct pending owner was already published.", error.Message);
    }

    [Fact]
    public async Task Reentrant_success_before_owner_publication_releases_slot()
    {
        await using var harness = CreateReentrantHarness(ReentrantResponseKind.Success);

        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 1));
        Assert.IsType<PendingUnaryResponse<int>>(harness.LastSentPendingResponse);

        harness.ReentrantResponse = ReentrantResponseKind.None;
        var held = InvokeTask(harness, request: 2);
        var heldMessageId = harness.LastRequestMessageId;
        await Assert.ThrowsAsync<ServiceException>(
            () => InvokeTask(harness, request: 3).WaitAsync(TestTimeout));

        harness.CompleteGeneric(heldMessageId);
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await held);
    }

    [Fact]
    public async Task Reentrant_remote_error_before_owner_publication_releases_slot()
    {
        await using var harness = CreateReentrantHarness(ReentrantResponseKind.Error);

        await Assert.ThrowsAsync<RemoteServiceException>(() => InvokeTask(harness, request: 1));

        harness.ReentrantResponse = ReentrantResponseKind.Success;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 2));
    }

    [Fact]
    public async Task Reentrant_connection_error_before_owner_publication_releases_slot()
    {
        await using var harness = CreateReentrantHarness(ReentrantResponseKind.ConnectionFailure);

        var error = await Assert.ThrowsAsync<ServiceConnectionException>(
            () => InvokeTask(harness, request: 1));
        Assert.Equal(ValueTaskTimeoutTestHarness.ConnectionFailureMessage, error.Message);

        harness.ReentrantResponse = ReentrantResponseKind.Success;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 2));
    }

    [Fact]
    public async Task Direct_timeout_releases_slot_and_sends_one_cancel()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            requestTimeout: TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAsync<ServiceTimeoutException>(
            () => InvokeTask(harness, request: 1).WaitAsync(TestTimeout));
        Assert.Equal(harness.LastRequestMessageId, await harness.WaitForCancelAsync(TestTimeout));
        Assert.Equal(1, harness.CancelCount);

        harness.ReentrantResponse = ReentrantResponseKind.Success;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 2));
    }

    [Fact]
    public async Task Pending_send_starts_timeout_after_send_and_sends_one_cancel()
    {
        var requestTimeout = TimeSpan.FromMilliseconds(80);
        await using var harness = new ValueTaskTimeoutTestHarness(
            requestTimeout,
            blockRequestSend: true);

        var call = InvokeTask(harness, request: 1);
        var messageId = harness.LastRequestMessageId;
        var premature = await Task.WhenAny(call, Task.Delay(requestTimeout * 3));
        Assert.NotSame(call, premature);

        harness.ReleaseRequestSend();
        await Assert.ThrowsAsync<ServiceTimeoutException>(() => call.WaitAsync(TestTimeout));
        Assert.Equal(messageId, await harness.WaitForCancelAsync(TestTimeout));
        Assert.Equal(1, harness.CancelCount);

        harness.ReentrantResponse = ReentrantResponseKind.Success;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 2));
    }

    private static ValueTaskTimeoutTestHarness CreateReentrantHarness(ReentrantResponseKind response) =>
        new(Timeout.InfiniteTimeSpan)
        {
            ReentrantResponse = response,
        };

    private static Task<int> InvokeTask(ValueTaskTimeoutTestHarness harness, int request) =>
        harness.Invoker.InvokeAsync<int, int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request);
}
