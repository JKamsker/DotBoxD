using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostCancellationCallbackReentrancyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task StopAsync_WhenTransportCancellationCallbackSynchronouslyStopsHost_CompletesOnce()
    {
        var transport = new ReentrantCancellationCallbackServerTransport();
        var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());
        transport.Host = host;

        await host.StartAsync();

        var stopTask = Task.Run(() => host.StopAsync());
        await transport.CallbackEntered.Task.WaitAsync(Timeout);
        await stopTask.WaitAsync(Timeout);

        Assert.Equal(1, transport.StopCalls);

        await host.DisposeAsync();
    }

    private sealed class ReentrantCancellationCallbackServerTransport : IServerTransport
    {
        private int _stopCalls;

        public RpcHost? Host { get; set; }

        public TaskCompletionSource CallbackEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public Task StartAsync(CancellationToken ct = default)
        {
            _ = ct.Register(OnLifetimeCanceled);
            return Task.CompletedTask;
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            throw new InvalidOperationException("The accept loop should be cancelled.");
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;

        private void OnLifetimeCanceled()
        {
            CallbackEntered.TrySetResult();
            Host!.StopAsync().GetAwaiter().GetResult();
        }
    }
}
