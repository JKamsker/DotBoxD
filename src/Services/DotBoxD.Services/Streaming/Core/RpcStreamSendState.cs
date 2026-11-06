using DotBoxD.Services.Diagnostics;

namespace DotBoxD.Services.Streaming.Core;

internal sealed class RpcStreamSendState : IDisposable
{
    private const int TerminalPending = 0;
    private const int TerminalRemoteCanceled = 1;
    private const int TerminalSettled = 2;
    private const int TerminalMissing = 3;

    private readonly CancellationTokenSource _cts;
    private readonly CancellationToken _token;
    private readonly SemaphoreSlim _credits = new(0);
    private int _availableCredits;
    private int _disposed;
    private int _removalClaimed;
    private int _terminalState;

    public RpcStreamSendState(int streamId, CancellationToken ownerToken)
    {
        StreamId = streamId;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
        _token = _cts.Token;
    }

    public int StreamId { get; }

    public CancellationToken Token => _token;

    public bool IsCancellationRequested => _token.IsCancellationRequested;

    public async Task WaitForCreditAsync(CancellationToken ct)
    {
        await _credits.WaitAsync(ct).ConfigureAwait(false);
        Interlocked.Decrement(ref _availableCredits);
    }

    public bool AddCredit(int count)
    {
        if (count <= 0)
        {
            return true;
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return true;
        }

        if (!TryReserveCredits(count))
        {
            return false;
        }

        try
        {
            _credits.Release(count);
        }
        catch (ObjectDisposedException)
        {
        }

        return true;
    }

    private bool TryReserveCredits(int count)
    {
        if (count > RpcStreamManager.WindowSize)
        {
            return false;
        }

        while (true)
        {
            var current = Volatile.Read(ref _availableCredits);
            if (current > RpcStreamManager.WindowSize - count)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _availableCredits, current + count, current) == current)
            {
                return true;
            }
        }
    }

    // The receiver keeps a tombstone after it sends StreamCancel until a terminal frame arrives,
    // so a remotely canceled stream must still end with exactly one terminal frame.
    internal bool IsRemoteCanceled => Volatile.Read(ref _terminalState) == TerminalRemoteCanceled;

    /// <summary>
    /// Records a remote StreamCancel. Returns <see langword="true"/> when the pump already ended
    /// without a terminal frame, so the caller must send the acknowledgement itself.
    /// </summary>
    internal bool MarkRemoteCanceled() =>
        Interlocked.CompareExchange(ref _terminalState, TerminalRemoteCanceled, TerminalPending) != TerminalPending &&
        Interlocked.CompareExchange(ref _terminalState, TerminalSettled, TerminalMissing) == TerminalMissing;

    /// <summary>
    /// Records that the pump stopped. Returns <see langword="true"/> when a remote cancel arrived and
    /// no terminal frame was sent, so the pump must send the acknowledgement.
    /// </summary>
    internal bool CompletePump(bool terminalSent)
    {
        var next = terminalSent ? TerminalSettled : TerminalMissing;
        if (Interlocked.CompareExchange(ref _terminalState, next, TerminalPending) == TerminalPending)
        {
            return false;
        }

        Volatile.Write(ref _terminalState, TerminalSettled);
        return !terminalSent;
    }

    internal bool TryClaimRemoval() => Interlocked.Exchange(ref _removalClaimed, 1) == 0;

    public void Cancel()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Outbound stream cancellation callback failed", ex);
        }
    }

    // Remote cancellation runs from frame processing. Registrations can block while awaiting a
    // response, so execute them independently of the worker that must process that response.
    public void CancelInBackground() =>
        _ = Task.Factory.StartNew(Cancel, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Cancel();
        _credits.Dispose();
        _cts.Dispose();
    }

    // Peer teardown must not let a caller's cancellation callback block its own completion.
    public void DisposeInBackground() =>
        _ = Task.Factory.StartNew(Dispose, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public void DisposeAfterCompletion()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _credits.Dispose();
        _cts.Dispose();
    }

    // A canceled wait can abandon a terminal send that still completes. After a remote cancel,
    // observe its real outcome (its token is canceled) so the acknowledgement is not duplicated.
    internal ValueTask<bool> WasTerminalSentAsync(Task? terminal)
    {
        if (terminal is null)
        {
            return new ValueTask<bool>(false);
        }

        if (terminal.IsCompleted || !IsRemoteCanceled)
        {
            return new ValueTask<bool>(terminal.IsCompletedSuccessfully);
        }

        return WaitForSendAsync(terminal);

        static async ValueTask<bool> WaitForSendAsync(Task send)
        {
            try
            {
                await send.ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
