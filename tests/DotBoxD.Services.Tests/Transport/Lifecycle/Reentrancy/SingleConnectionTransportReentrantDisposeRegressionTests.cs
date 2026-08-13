using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class SingleConnectionTransportReentrantDisposeRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ClientDisposeAsync_ReentrantOwnedConnectionCleanupSharesTerminal()
    {
        var channel = new ReentrantFaultingChannel();
        var transport = new SingleConnectionTransport(channel, ownsConnection: true);
        channel.SetReentrantDispose(transport.DisposeAsync);

        await AssertReentrantDisposeSharesOwnedCleanupFailureAsync(transport.DisposeAsync, channel);
    }

    [Fact]
    public async Task ServerDisposeAsync_ReentrantOwnedConnectionCleanupSharesTerminal()
    {
        var channel = new ReentrantFaultingChannel();
        var transport = new SingleConnectionServerTransport(channel, ownsConnection: true);
        channel.SetReentrantDispose(transport.DisposeAsync);

        await AssertReentrantDisposeSharesOwnedCleanupFailureAsync(transport.DisposeAsync, channel);
    }

    private static async Task AssertReentrantDisposeSharesOwnedCleanupFailureAsync(
        Func<ValueTask> disposeAsync,
        ReentrantFaultingChannel channel)
    {
        var outerDispose = disposeAsync().AsTask();
        await channel.ReentrantDisposeCompleted.WaitAsync(Timeout);

        var outerFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => outerDispose.WaitAsync(Timeout));
        var nestedFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => channel.NestedDispose.WaitAsync(Timeout));

        Assert.Equal(1, channel.DisposeEntries);
        Assert.Same(channel.CleanupFailure, outerFailure);
        Assert.Same(channel.CleanupFailure, nestedFailure);
    }

    private sealed class ReentrantFaultingChannel : IRpcChannel
    {
        private readonly TaskCompletionSource _reentrantDisposeCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<ValueTask>? _reentrantDispose;
        private int _disposeEntries;

        public InvalidOperationException CleanupFailure { get; } =
            new("owned channel cleanup failed");

        public int DisposeEntries => Volatile.Read(ref _disposeEntries);

        public bool IsConnected => true;

        public Task NestedDispose { get; private set; } = Task.CompletedTask;

        public string RemoteEndpoint => "reentrant://single-connection";

        public Task ReentrantDisposeCompleted => _reentrantDisposeCompleted.Task;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Payload.Empty);

        public void SetReentrantDispose(Func<ValueTask> disposeAsync) =>
            _reentrantDispose = disposeAsync;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeEntries) == 1)
            {
                NestedDispose = _reentrantDispose!().AsTask();
                _reentrantDisposeCompleted.TrySetResult();
            }

            return ValueTask.FromException(CleanupFailure);
        }
    }
}
