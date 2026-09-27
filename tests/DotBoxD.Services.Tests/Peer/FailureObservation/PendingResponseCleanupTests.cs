using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.FailureObservation;

public sealed class PendingResponseCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Abandoned_response_releases_its_frame(bool completeFirst)
    {
        using var payload = Payload.Rent(16);
        using var response = new ReceivedResponse(new RpcResponse { IsSuccess = true }, payload.Memory, payload, stream: null);
        var source = new TaskCompletionSource<ReceivedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completeFirst)
        {
            source.SetResult(response);
        }

        ReceivedResponse.DisposeWhenAvailable(source.Task);
        if (!completeFirst)
        {
            source.SetResult(response);
        }

        var deadline = Environment.TickCount64 + 5_000;
        while (Record.Exception(() => _ = payload.Memory) is null && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Throws<ObjectDisposedException>(() => _ = payload.Memory);
        Assert.Same(response, await source.Task);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unary_success_remains_available(bool completeFirst)
    {
        var pending = new PendingUnaryResponse<int>(1);
        if (completeFirst)
        {
            pending.SetResult(42);
        }

        pending.DisposeResultWhenAvailable();
        if (!completeFirst)
        {
            pending.SetResult(42);
        }

        Assert.Equal(42, await pending.Task);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cancellation_remains_cancellation(bool unary, bool completeFirst)
    {
        using var requests = new PendingRequests();
        var received = new PendingReceivedResponse(requests, 1);
        var response = new PendingUnaryResponse<int>(1);
        IPendingResponse pending = unary ? response : received;
        Task task = unary ? response.Task : received.Task;
        if (completeFirst)
        {
            pending.TrySetCanceled(PendingCancellationKind.Caller);
        }

        pending.DisposeResultWhenAvailable();
        if (!completeFirst)
        {
            pending.TrySetCanceled(PendingCancellationKind.Caller);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
    }
}
