using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostConcurrentDisposalRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisposeAsync_WhenConcurrentStopFails_DoesNotReportSecondCleanupSuccessEarly()
    {
        var stopFailure = new InvalidOperationException("stop sentinel");
        var transport = new GatedFaultingStopServerTransport(stopFailure);
        var host = RpcHost.Listen(transport, new MessagePackRpcSerializer());

        await host.StartAsync();

        var firstDispose = host.DisposeAsync().AsTask();
        await transport.StopEntered.WaitAsync(Timeout);

        var secondDispose = host.DisposeAsync().AsTask();

        try
        {
            Assert.False(
                secondDispose.IsCompleted,
                "The second concurrent DisposeAsync completed before StopAsync finished.");

            transport.ReleaseStop();

            var firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => firstDispose.WaitAsync(Timeout));
            var secondFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => secondDispose.WaitAsync(Timeout));

            Assert.Same(stopFailure, firstFailure);
            Assert.Same(stopFailure, secondFailure);
        }
        finally
        {
            transport.ReleaseStop();
            await IgnoreAsync(firstDispose);
            await IgnoreAsync(secondDispose);
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
            // Test cleanup only; the assertions above own the expected terminal.
        }
    }

    private sealed class GatedFaultingStopServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource _acceptReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowStop =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Exception _stopFailure;

        public GatedFaultingStopServerTransport(Exception stopFailure) => _stopFailure = stopFailure;

        public Task StopEntered => _stopEntered.Task;

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            using var registration = ct.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                _acceptReleased);

            await _acceptReleased.Task.ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _acceptReleased.TrySetResult();
            _stopEntered.TrySetResult();
            await _allowStop.Task.ConfigureAwait(false);
            throw _stopFailure;
        }

        public ValueTask DisposeAsync() => default;

        public void ReleaseStop() => _allowStop.TrySetResult();
    }
}
