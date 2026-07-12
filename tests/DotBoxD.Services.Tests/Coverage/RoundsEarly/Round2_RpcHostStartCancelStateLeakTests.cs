using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.RoundsEarly;

/// <summary>
/// Regression coverage for pre-cancelled <see cref="RpcHost"/>.<c>StartAsync</c>.
///
/// The host must honor a caller token that is already cancelled before it creates host lifecycle
/// state or touches the listener. Otherwise a pre-cancelled start can start and stop the listener
/// just to report cancellation, and earlier fixes had to clean up leaked <c>_cts</c> state after
/// that recovery path.
///
/// Desired behaviour: a pre-cancelled start throws cancellation without starting or stopping the
/// listener, and the host remains restartable with a live token.
/// </summary>
public sealed class Round2_RpcHostStartCancelStateLeakTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task StartAsync_WithPreCancelledToken_DoesNotLeaveHostMarkedAlreadyRunning()
    {
        var transport = new YieldingStartServerTransport();
        await using var host = RpcHost.Listen(transport, NewSerializer());

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.StartAsync(preCancelled.Token).WaitAsync(Timeout));
        Assert.Equal(0, transport.StartCalls);
        Assert.Equal(0, transport.StopCalls);

        // The failed start must have left the host restartable.
        var secondStartFailure = await Record.ExceptionAsync(
            () => host.StartAsync().WaitAsync(Timeout));

        Assert.Null(secondStartFailure);
        Assert.Equal(1, transport.StartCalls);
    }

    /// <summary>
    /// Transport whose <c>StartAsync</c> always succeeds (after one <see cref="Task.Yield"/>) and
    /// ignores its token, and whose <c>AcceptAsync</c> parks on its token so a started accept loop
    /// stays alive without faulting until shutdown.
    /// </summary>
    private sealed class YieldingStartServerTransport : IServerTransport
    {
        private int _startCalls;
        private int _stopCalls;

        public int StartCalls => Volatile.Read(ref _startCalls);

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public async Task StartAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _startCalls);

            // Complete asynchronously but successfully, ignoring the (possibly cancelled) token so
            // StartAsync reaches its second lock instead of the catch block.
            await Task.Yield();
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            // Park until the host cancels us during shutdown; never hand back a connection.
            var parked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), parked))
            {
                await parked.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }
}
