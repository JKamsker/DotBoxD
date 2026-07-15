using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.RoundsLate;

public sealed class RpcHostDisposeFailureOrderingTests
{
    [Fact]
    public async Task DisposeAsync_WhenStopAndListenerDisposeThrow_PreservesStopFailure()
    {
        var stopFailure = new InvalidOperationException("stop sentinel");
        var disposeFailure = new ApplicationException("dispose sentinel");
        var transport = new ThrowingStopAndDisposeServerTransport(stopFailure, disposeFailure);
        var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());

        await host.StartAsync();

        var escaped = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.DisposeAsync());

        Assert.Same(stopFailure, escaped);
        Assert.Equal(1, transport.StopCalls);
        Assert.Equal(1, transport.DisposeCalls);
        Assert.Contains(disposeFailure, escaped.Data.Values.Cast<object>());
    }

    private sealed class ThrowingStopAndDisposeServerTransport : IServerTransport
    {
        private readonly Exception _stopFailure;
        private readonly Exception _disposeFailure;
        private readonly TaskCompletionSource<bool> _acceptReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCalls;
        private int _stopCalls;

        public ThrowingStopAndDisposeServerTransport(Exception stopFailure, Exception disposeFailure)
        {
            _stopFailure = stopFailure;
            _disposeFailure = disposeFailure;
        }

        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            using var registration = ct.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                _acceptReleased);
            await _acceptReleased.Task.ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            _acceptReleased.TrySetResult(true);
            return Task.FromException(_stopFailure);
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCalls);
            return ValueTask.FromException(_disposeFailure);
        }
    }
}
