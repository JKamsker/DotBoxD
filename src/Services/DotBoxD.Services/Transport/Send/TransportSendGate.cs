namespace DotBoxD.Services.Transport;

internal static class TransportSendGate
{
    // Fast acquisition deliberately does not inspect the caller token. Callers must recheck it
    // inside their release-protected region, because transport disposal may inject a terminal permit
    // between acquisition and that check.
    private static bool TryEnter(SemaphoreSlim gate) => gate.Wait(0);

    public static ValueTask WaitAsync(SemaphoreSlim gate, CancellationToken callerToken)
    {
        if (TryEnter(gate))
        {
            return default;
        }

        return new ValueTask(gate.WaitAsync(callerToken));
    }

    public static void WakeDisposedWaiters(SemaphoreSlim gate)
    {
        // Disposal is published before this terminal permit. It may coexist only with the old
        // in-flight owner: each awakened sender observes disposal before I/O and releases in its
        // finally block to wake the next waiter, leaving exactly one possible surplus release.
        if (gate.CurrentCount != 0)
        {
            return;
        }

        try
        {
            gate.Release();
        }
        catch (SemaphoreFullException)
        {
            // The gate was idle or an in-flight owner published its release first.
        }
    }

    public static void ReleaseAfterSend(SemaphoreSlim gate, ref int disposed)
    {
        try
        {
            gate.Release();
        }
        catch (SemaphoreFullException) when (Volatile.Read(ref disposed) != 0)
        {
            // Disposal already supplied the terminal permit that wakes blocked senders.
        }
    }
}
