namespace DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// A wall-time <see cref="CancellationTokenSource"/> shared for one sandbox-context generation.
/// The resource meter owns a fixed absolute deadline, so binding dispatch arms this source once
/// and temporarily links cancelable run tokens through allocation-free registrations.
/// </summary>
/// <remarks>
/// The owning context disposes this source at the execution boundary. Public wall-time token
/// requests receive a separate caller-owned source and cannot cancel or dispose this internal timer.
/// </remarks>
internal sealed class SharedWallTimeTokenSource : CancellationTokenSource
{
    private Timer? _deadline;

    public void CancelIgnoringCallbackExceptions()
    {
        try
        {
            Cancel(throwOnFirstException: false);
        }
        catch (ObjectDisposedException)
        {
            // A timer callback may already be queued when its context is disposed.
        }
        catch (AggregateException)
        {
            // Binding-owned callbacks must not make cancellation of the caller's run token fail.
        }
    }

    public void ArmDeadline(TimeSpan remaining)
    {
        if (!IsCancellationRequested)
        {
            _deadline ??= CreateDeadlineTimer();
            _deadline.Change(remaining, Timeout.InfiniteTimeSpan);
        }
    }

    private Timer CreateDeadlineTimer()
    {
        if (ExecutionContext.IsFlowSuppressed())
        { return CreateTimer(); }
        using var flow = ExecutionContext.SuppressFlow();
        return CreateTimer();
    }

    private Timer CreateTimer()
        => new(static state => ((SharedWallTimeTokenSource)state!).CancelIgnoringCallbackExceptions(),
            this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        { _deadline?.Dispose(); }
        base.Dispose(disposing);
    }
}
