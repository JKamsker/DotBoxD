using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostCancellationCallbackStopTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task StopAsync_WhenTransportCancellationCallbackThrows_StillStopsTransport()
    {
        var transport = new ThrowingCancellationCallbackServerTransport();
        var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());

        try
        {
            await host.StartAsync();

            await host.StopAsync().WaitAsync(Timeout);

            Assert.Equal(1, transport.StopCalls);
        }
        finally
        {
            await IgnoreAsync(host.DisposeAsync().AsTask());
        }
    }

    private static async Task IgnoreAsync(Task task)
    {
        try
        {
            await task.WaitAsync(Timeout).ConfigureAwait(false);
        }
        catch
        {
            // Test cleanup only; the assertion owns the expected terminal.
        }
    }

    private sealed class ThrowingCancellationCallbackServerTransport : IServerTransport
    {
        private int _stopCalls;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public Task StartAsync(CancellationToken ct = default)
        {
            _ = ct.Register(static () => throw new InvalidOperationException("stop sentinel"));
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
    }
}
