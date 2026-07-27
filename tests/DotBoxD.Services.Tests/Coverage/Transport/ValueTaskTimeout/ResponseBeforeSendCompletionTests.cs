using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

/// <summary>
/// Pins the response-wins path for a backpressured request send. The response removes correlation
/// state, while the send wrapper retains the admission slot and any pooled source until send ends.
/// </summary>
public sealed class ResponseBeforeSendCompletionTests
{
    [Fact]
    public async Task Pooled_unary_holds_slot_until_send_completes_and_reuses_source()
    {
        await using var harness = CreateBlockedHarness();

        var call = InvokePooledUnary(harness, request: 1).AsTask();
        var messageId = harness.LastRequestMessageId;
        var source = harness.GetPendingResponse(messageId);
        Assert.IsType<PendingValueTaskUnaryResponse<ResponseValue>>(source);

        harness.CompleteGeneric(messageId, ResponseValue.First);

        await AssertStillPendingAsync(call);
        await AssertPendingLimitAsync(InvokePooledUnary(harness, request: 2).AsTask());

        harness.ReleaseRequestSend();
        Assert.Equal(ResponseValue.First, await call);

        var followUp = InvokePooledUnary(harness, request: 3);
        messageId = harness.LastRequestMessageId;
        Assert.Same(source, harness.GetPendingResponse(messageId));
        harness.CompleteGeneric(messageId, ResponseValue.FollowUp);

        Assert.Equal(ResponseValue.FollowUp, await followUp);
    }

    [Fact]
    public async Task Pooled_no_response_holds_slot_until_send_completes_and_reuses_source()
    {
        await using var harness = CreateBlockedHarness();

        var call = InvokePooledNoResponse(harness, request: 1).AsTask();
        var messageId = harness.LastRequestMessageId;
        var source = harness.GetPendingResponse(messageId);
        Assert.IsType<PendingValueTaskNoResponse>(source);

        harness.CompleteNoResponse(messageId);

        await AssertStillPendingAsync(call);
        await AssertPendingLimitAsync(InvokePooledNoResponse(harness, request: 2).AsTask());

        harness.ReleaseRequestSend();
        await call;

        var followUp = InvokePooledNoResponse(harness, request: 3);
        messageId = harness.LastRequestMessageId;
        Assert.Same(source, harness.GetPendingResponse(messageId));
        harness.CompleteNoResponse(messageId);

        await followUp;
    }

    [Fact]
    public async Task Task_unary_holds_slot_until_send_completes_then_admits_follow_up()
    {
        await using var harness = CreateBlockedHarness();

        var call = InvokeTaskUnary(harness, request: 1);
        var messageId = harness.LastRequestMessageId;
        Assert.IsType<PendingUnaryResponse<ResponseValue>>(
            harness.GetPendingResponse(messageId));

        harness.CompleteGeneric(messageId, ResponseValue.First);

        await AssertStillPendingAsync(call);
        await AssertPendingLimitAsync(InvokeTaskUnary(harness, request: 2));

        harness.ReleaseRequestSend();
        Assert.Equal(ResponseValue.First, await call);

        var followUp = InvokeTaskUnary(harness, request: 3);
        messageId = harness.LastRequestMessageId;
        harness.CompleteGeneric(messageId, ResponseValue.FollowUp);

        Assert.Equal(ResponseValue.FollowUp, await followUp);
    }

    [Fact]
    public async Task General_no_response_holds_slot_until_send_completes_then_admits_follow_up()
    {
        await using var harness = CreateBlockedHarness();

        var call = InvokeTaskNoResponse(harness, request: 1);
        var messageId = harness.LastRequestMessageId;
        Assert.IsType<PendingReceivedResponse>(harness.GetPendingResponse(messageId));

        harness.CompleteNoResponse(messageId);

        await AssertStillPendingAsync(call);
        await AssertPendingLimitAsync(InvokeTaskNoResponse(harness, request: 2));

        harness.ReleaseRequestSend();
        await call;

        var followUp = InvokeTaskNoResponse(harness, request: 3);
        messageId = harness.LastRequestMessageId;
        harness.CompleteNoResponse(messageId);

        await followUp;
    }

    private static ValueTaskTimeoutTestHarness CreateBlockedHarness() =>
        new(
            Timeout.InfiniteTimeSpan,
            maxPendingRequests: 1,
            blockRequestSend: true);

    private static ValueTask<ResponseValue> InvokePooledUnary(
        ValueTaskTimeoutTestHarness harness,
        int request) =>
        harness.Invoker.InvokeValueAsync<int, ResponseValue>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request);

    private static ValueTask InvokePooledNoResponse(
        ValueTaskTimeoutTestHarness harness,
        int request) =>
        harness.Invoker.InvokeValueAsync<int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request);

    private static Task<ResponseValue> InvokeTaskUnary(
        ValueTaskTimeoutTestHarness harness,
        int request) =>
        harness.Invoker.InvokeAsync<int, ResponseValue>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request);

    private static Task InvokeTaskNoResponse(
        ValueTaskTimeoutTestHarness harness,
        int request) =>
        harness.Invoker.InvokeAsync<int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request);

    private static async Task AssertPendingLimitAsync(Task call)
    {
        var error = await Assert.ThrowsAsync<ServiceException>(() => call);
        Assert.Equal("Maximum pending requests reached.", error.Message);
    }

    private static async Task AssertStillPendingAsync(Task call)
    {
        Assert.False(call.IsCompleted);
        Assert.NotSame(call, await Task.WhenAny(call, Task.Delay(TimeSpan.FromMilliseconds(20))));
    }

    private enum ResponseValue
    {
        First = 17,
        FollowUp = 23,
    }
}
