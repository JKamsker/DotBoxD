using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.RoundsMiddle;

/// <summary>
/// RED regression for the recovery-path exception masking (and linked-CTS leak) in
/// <see cref="RpcHost"/>.<c>StartAsync</c>.
///
/// When the caller-supplied <see cref="CancellationToken"/> is cancelled after the transport's
/// <c>StartAsync</c> succeeds but before the host completes startup, <c>StartAsync</c> enters the
/// <c>cts.IsCancellationRequested</c> recovery branch (RpcHost.cs ~line 179): it sets
/// <c>startFailure = InvalidOperationException("Host start was stopped before it completed.")</c>,
/// arms <c>disposeCts = true</c>, then runs the best-effort recovery
/// <c>await _listener.StopAsync(CancellationToken.None)</c> inside a <c>try { ... } finally { _starting = false; }</c>.
/// Only AFTER that <c>try</c> block does it run <c>if (disposeCts) cts.Dispose();</c> and
/// <c>throw startFailure;</c>.
///
/// The defect: if the recovery <c>_listener.StopAsync</c> THROWS, the exception unwinds through
/// the <c>finally</c> (which only resets <c>_starting</c>), skipping BOTH <c>cts.Dispose()</c>
/// (leaking the linked CTS) AND <c>throw startFailure</c>. The caller therefore observes the
/// raw <c>StopAsync</c> exception ("stop fault") instead of the intended
/// "Host start was stopped before it completed.".
///
/// Desired behaviour: the best-effort recovery stop must neither mask the real start outcome nor
/// leak the linked CTS. The thrown exception's message must report that the start was stopped
/// before it completed, NOT the recovery-stop fault. The suggested fix (wrap the recovery
/// <c>StopAsync</c> in try/catch and move <c>cts.Dispose()</c> into a finally) makes this green.
///
/// Fully deterministic and single-threaded: the host's listener-started test hook cancels the
/// caller token after <c>_listener.StartAsync</c> succeeds and before the second lifecycle lock.
/// The transport's <c>StopAsync</c> always throws synchronously after a yield.
/// </summary>
public sealed class Round3_StartRecoveryStopThrowsCtsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task StartAsync_CancelledAfterListenerStart_RecoveryStopThrows_StillReportsStartStopped()
    {
        var transport = new StopThrowingServerTransport();
        await using var host = RpcHost.Listen(transport, NewSerializer());

        using var cts = new CancellationTokenSource();
        host._onListenerStartedForTest = () =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        };

        // The transport start succeeds, then the caller token is cancelled before StartAsync
        // completes. StartAsync enters the recovery branch and best-effort-stops the just-started
        // listener. That stop throws "stop fault". On the unfixed code the recovery exception
        // escapes and masks the intended start outcome, so this throws an InvalidOperationException
        // whose message is "stop fault" instead of "Host start was stopped before it completed.".
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync(cts.Token).WaitAsync(Timeout));

        // The recovery stop did run (proving we are exercising the path under test)...
        Assert.True(transport.StopCalls > 0);

        // ...but its fault must NOT be what the caller sees. The start outcome must win.
        Assert.DoesNotContain("stop fault", failure.Message);
        Assert.Contains("stopped before it completed", failure.Message);
    }

    /// <summary>
    /// Transport whose <c>StartAsync</c> always succeeds (after one <see cref="Task.Yield"/>) and
    /// ignores its token so <c>StartAsync</c> reaches its second lock, and whose <c>StopAsync</c>
    /// always throws <see cref="InvalidOperationException"/> ("stop fault").
    /// </summary>
    private sealed class StopThrowingServerTransport : IServerTransport
    {
        private int _stopCalls;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public async Task StartAsync(CancellationToken ct = default)
        {
            // Complete asynchronously but successfully, before the test hook cancels the token, so
            // StartAsync reaches its recovery path instead of the catch block.
            await Task.Yield();
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            // The recovery branch never launches the accept loop; park defensively if ever called.
            var parked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), parked))
            {
                await parked.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            await Task.Yield();
            throw new InvalidOperationException("stop fault");
        }

        public ValueTask DisposeAsync() => default;
    }
}
