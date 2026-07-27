using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class SingleConnectionTransportConcurrentDisposeRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ClientDisposeAsync_ConcurrentCallWaitsForOwnedConnectionCleanupFailure()
    {
        var channel = new GatedFaultingChannel();
        var transport = new SingleConnectionTransport(channel, ownsConnection: true);

        await AssertConcurrentDisposeSharesOwnedCleanupFailureAsync(transport.DisposeAsync, channel);
    }

    [Fact]
    public async Task ServerDisposeAsync_ConcurrentCallWaitsForOwnedConnectionCleanupFailure()
    {
        var channel = new GatedFaultingChannel();
        var transport = new SingleConnectionServerTransport(channel, ownsConnection: true);

        await AssertConcurrentDisposeSharesOwnedCleanupFailureAsync(transport.DisposeAsync, channel);
    }

    private static async Task AssertConcurrentDisposeSharesOwnedCleanupFailureAsync(
        Func<ValueTask> disposeAsync,
        GatedFaultingChannel channel)
    {
        var firstDispose = disposeAsync().AsTask();
        await channel.DisposeEntered.WaitAsync(Timeout);

        var secondDispose = disposeAsync().AsTask();
        var secondStillWaitingBeforeRelease = !secondDispose.IsCompleted;

        channel.ReleaseDispose();

        var firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => firstDispose.WaitAsync(Timeout));

        Assert.True(
            secondStillWaitingBeforeRelease,
            "Concurrent disposal must wait for the owned channel cleanup already in flight.");

        var secondFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => secondDispose.WaitAsync(Timeout));

        Assert.Same(channel.CleanupFailure, firstFailure);
        Assert.Same(channel.CleanupFailure, secondFailure);
    }

    private sealed class GatedFaultingChannel : IRpcChannel
    {
        private readonly TaskCompletionSource _disposeEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseDispose =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DisposeEntered => _disposeEntered.Task;

        public InvalidOperationException CleanupFailure { get; } =
            new("owned channel cleanup failed");

        public bool IsConnected => true;

        public string RemoteEndpoint => "gated://single-connection";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Payload.Empty);

        public async ValueTask DisposeAsync()
        {
            _disposeEntered.TrySetResult();
            await _releaseDispose.Task.ConfigureAwait(false);
            throw CleanupFailure;
        }

        public void ReleaseDispose() => _releaseDispose.TrySetResult();
    }
}
