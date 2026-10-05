using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostListenerDisposalReentrancyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisposeAsync_WhenListenerDisposalReenters_OnlyDisposesListenerOnce()
    {
        var transport = new ReentrantDisposalServerTransport();
        var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());
        transport.Host = host;

        var firstDispose = host.DisposeAsync().AsTask();

        await firstDispose.WaitAsync(Timeout);
        await transport.ReentrantDispose.WaitAsync(Timeout);

        Assert.Equal(1, transport.DisposeCallCount);
    }

    private sealed class ReentrantDisposalServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<Task> _reentrantDispose =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCallCount;

        public RpcHost? Host { get; set; }

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public Task ReentrantDispose => _reentrantDispose.Task.Unwrap();

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            Task.FromCanceled<IRpcChannel>(ct);

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeCallCount) == 1)
            {
                var reentrantDispose = Host!.DisposeAsync().AsTask();
                _reentrantDispose.TrySetResult(reentrantDispose);
            }

            return default;
        }
    }
}
