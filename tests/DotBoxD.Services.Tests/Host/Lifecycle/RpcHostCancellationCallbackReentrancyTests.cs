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

    [Fact]
    public async Task StopAsync_WhenLastRegisteredCallbackJoinsStop_StopsListenerBeforeJoiningAccept()
    {
        var transport = new ReverseRegistrationServerTransport();
        var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());
        transport.Host = host;
        await host.StartAsync();
        await transport.AcceptEntered.Task.WaitAsync(Timeout);

        var stop = Task.Run(() => host.StopAsync());
        try
        {
            await stop.WaitAsync(Timeout);
            Assert.Equal(1, transport.StopCalls);
        }
        finally
        {
            transport.ReleaseAccept();
            await IgnoreFailureAsync(stop);
            await IgnoreFailureAsync(host.DisposeAsync().AsTask());
        }
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        { await task.WaitAsync(Timeout); }
        catch { }
    }

    private sealed class ReverseRegistrationServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<IRpcChannel> _accept = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RpcHost? Host { get; set; }
        public TaskCompletionSource AcceptEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCalls { get; private set; }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            ct.Register(() => _accept.TrySetCanceled(ct));
            ct.Register(() => Host!.StopAsync().GetAwaiter().GetResult());
            AcceptEntered.TrySetResult();
            return _accept.Task;
        }
        public void ReleaseAccept() => _accept.TrySetCanceled();
        public Task StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            ReleaseAccept();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
