using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostStartCancellationRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task StartAsync_WithPreCanceledToken_DoesNotStartOrStopListener()
    {
        var transport = new RecordingStartTransport();
        await using var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = await Record.ExceptionAsync(() =>
            host.StartAsync(cts.Token).WaitAsync(Timeout));

        var observed = string.Join(
            ", ",
            $"exception={exception?.GetType().Name ?? "(none)"}",
            $"startCalls={transport.StartCalls}",
            $"startTokenCanceled={transport.StartTokenCanceled}",
            $"stopCalls={transport.StopCalls}",
            $"stopTokenCanceled={transport.StopTokenCanceled}",
            $"acceptCalls={transport.AcceptCalls}");
        Assert.True(
            exception is OperationCanceledException &&
            transport.StartCalls == 0 &&
            transport.StopCalls == 0 &&
            transport.AcceptCalls == 0,
            observed);
    }

    private sealed class RecordingStartTransport : IServerTransport
    {
        private int _acceptCalls;
        private int _startCalls;
        private int _startTokenCanceled;
        private int _stopCalls;
        private int _stopTokenCanceled;

        public int AcceptCalls => Volatile.Read(ref _acceptCalls);

        public int StartCalls => Volatile.Read(ref _startCalls);

        public bool StartTokenCanceled => Volatile.Read(ref _startTokenCanceled) != 0;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public bool StopTokenCanceled => Volatile.Read(ref _stopTokenCanceled) != 0;

        public Task StartAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _startCalls);
            if (ct.IsCancellationRequested)
            {
                Volatile.Write(ref _startTokenCanceled, 1);
            }

            return Task.CompletedTask;
        }

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _acceptCalls);
            return Task.FromException<IRpcChannel>(
                new InvalidOperationException("The accept loop should not start."));
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            if (ct.IsCancellationRequested)
            {
                Volatile.Write(ref _stopTokenCanceled, 1);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }
}
