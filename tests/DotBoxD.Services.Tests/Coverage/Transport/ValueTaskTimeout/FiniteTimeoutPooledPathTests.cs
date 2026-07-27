using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class FiniteTimeoutPooledPathTests
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan SuccessRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Generic_finite_timeout_success_uses_pooled_source()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(SuccessRequestTimeout);

        var call = harness.Invoker.InvokeValueAsync<int, int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 3);
        var pending = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteGeneric(harness.LastRequestMessageId);

        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await call);
        Assert.IsType<PendingValueTaskUnaryResponse<int>>(pending);
    }

    [Fact]
    public async Task Generic_finite_timeout_faults_and_releases_slot()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(RequestTimeout);

        var call = harness.Invoker.InvokeValueAsync<int, int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 3);
        var pending = harness.GetPendingResponse(harness.LastRequestMessageId);
        var task = call.AsTask();

        var error = await Assert.ThrowsAsync<ServiceTimeoutException>(
            () => task.WaitAsync(TestTimeout));
        Assert.Contains(
            $"{ValueTaskTimeoutTestHarness.Service}.{ValueTaskTimeoutTestHarness.Method}",
            error.Message);
        Assert.False(task.IsCanceled);
        Assert.Equal(harness.LastRequestMessageId, await harness.WaitForCancelAsync(TestTimeout));
        Assert.Equal(1, harness.CancelCount);

        var followUp = harness.Invoker.InvokeValueAsync<int, int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 4);
        harness.CompleteGeneric(harness.LastRequestMessageId);
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await followUp);
        Assert.IsType<PendingValueTaskUnaryResponse<int>>(pending);
    }

    [Fact]
    public async Task No_response_finite_timeout_success_uses_pooled_source()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(SuccessRequestTimeout);

        var call = harness.Invoker.InvokeValueAsync<int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 3);
        var pending = harness.GetPendingResponse(harness.LastRequestMessageId);
        harness.CompleteNoResponse(harness.LastRequestMessageId);

        await call;
        Assert.IsType<PendingValueTaskNoResponse>(pending);
    }

    [Fact]
    public async Task No_response_finite_timeout_faults_and_releases_slot()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(RequestTimeout);

        var call = harness.Invoker.InvokeValueAsync<int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 3);
        var pending = harness.GetPendingResponse(harness.LastRequestMessageId);
        var task = call.AsTask();

        var error = await Assert.ThrowsAsync<ServiceTimeoutException>(
            () => task.WaitAsync(TestTimeout));
        Assert.Contains(
            $"{ValueTaskTimeoutTestHarness.Service}.{ValueTaskTimeoutTestHarness.Method}",
            error.Message);
        Assert.False(task.IsCanceled);
        Assert.Equal(harness.LastRequestMessageId, await harness.WaitForCancelAsync(TestTimeout));
        Assert.Equal(1, harness.CancelCount);

        var followUp = harness.Invoker.InvokeValueAsync<int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 4);
        harness.CompleteNoResponse(harness.LastRequestMessageId);
        await followUp;
        Assert.IsType<PendingValueTaskNoResponse>(pending);
    }
}
