using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class RpcHostLifecycleRegressionTests
{
    [Fact]
    public async Task DisposeDuringStart_DoesNotStartAcceptLoopAfterDispose()
    {
        var transport = new DelayedStartServerTransport();
        await using var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());

        var startTask = host.StartAsync();
        await transport.StartEntered.WaitAsync(TimeSpan.FromSeconds(1));

        await host.DisposeAsync();
        transport.AllowStart();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => startTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, transport.AcceptCalls);
        Assert.True(transport.StopCalls > 0);
    }

    private sealed class DelayedStartServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<bool> _startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowStart =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _acceptCalls;
        private int _stopCalls;

        public Task StartEntered => _startEntered.Task;

        public int AcceptCalls => Volatile.Read(ref _acceptCalls);

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public async Task StartAsync(CancellationToken ct = default)
        {
            _startEntered.TrySetResult(true);
            await _allowStart.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public Task<IConnection> AcceptAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _acceptCalls);
            throw new InvalidOperationException("The accept loop should not start.");
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;

        public void AllowStart() => _allowStart.TrySetResult(true);
    }
}
