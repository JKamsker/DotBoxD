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
}
