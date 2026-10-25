using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostListenerDisposalReentrancyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisposeAsync_WhenListenerDisposalAwaitsReentry_DoesNotDeadlock()
    {
        var transport = new ReentrantDisposalServerTransport();
        var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());
        transport.Host = host;

        var firstDispose = host.DisposeAsync().AsTask();

        await transport.ListenerDisposalEntered.WaitAsync(Timeout);

        var concurrentDispose = host.DisposeAsync().AsTask();
        Assert.Same(firstDispose, concurrentDispose);
        Assert.False(concurrentDispose.IsCompleted);

        transport.CompleteListenerDisposal();
        await firstDispose.WaitAsync(Timeout);

        Assert.Equal(1, transport.DisposeCallCount);
    }

    [Fact]
    public async Task DisposeAsync_WhenListenerFlowsReentryAfterFailure_ReportsDisposeFailure()
    {
        var transport = new FaultingReentrantDisposalServerTransport();
        var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());
        transport.Host = host;

        var disposeTask = host.DisposeAsync().AsTask();
        await transport.ListenerDisposalEntered.WaitAsync(Timeout);

        transport.FailListenerDisposal();
        await Assert.ThrowsAsync<InvalidOperationException>(() => disposeTask);

        transport.ReleaseReentry();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.FlowedReentry.WaitAsync(Timeout));
    }

    private sealed class ReentrantDisposalServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource _listenerDisposalEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _listenerDisposalCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCallCount;

        public RpcHost? Host { get; set; }

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public Task ListenerDisposalEntered => _listenerDisposalEntered.Task;

        public void CompleteListenerDisposal() => _listenerDisposalCompletion.TrySetResult();

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            Task.FromCanceled<IRpcChannel>(ct);

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeCallCount) == 1)
            {
                await Host!.DisposeAsync();
                _listenerDisposalEntered.TrySetResult();
                await _listenerDisposalCompletion.Task;
            }
        }
    }

    private sealed class FaultingReentrantDisposalServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource _listenerDisposalEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _listenerDisposalCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseReentry =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RpcHost? Host { get; set; }

        public Task ListenerDisposalEntered => _listenerDisposalEntered.Task;

        public Task FlowedReentry { get; private set; } = Task.CompletedTask;

        public void FailListenerDisposal() => _listenerDisposalCompletion.TrySetResult();

        public void ReleaseReentry() => _releaseReentry.TrySetResult();

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            Task.FromCanceled<IRpcChannel>(ct);

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            FlowedReentry = Task.Run(async () =>
            {
                await _releaseReentry.Task;
                await Host!.DisposeAsync();
            });
            _listenerDisposalEntered.TrySetResult();
            await _listenerDisposalCompletion.Task;
            throw new InvalidOperationException("Expected listener disposal failure.");
        }
    }
}
