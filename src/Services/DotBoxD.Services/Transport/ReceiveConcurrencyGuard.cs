namespace DotBoxD.Services.Transport;

internal static class ReceiveConcurrencyGuard
{
    private const int Idle = 0;
    private const int Active = 1;
    private const int Disposed = 2;
    private const int DisposedActive = Disposed | Active;

    public static void Enter(ref int receiveLifecycle, string channelName)
    {
        var observed = Interlocked.CompareExchange(ref receiveLifecycle, Active, Idle);
        if (observed == Idle)
        {
            return;
        }

        if ((observed & Disposed) != 0)
        {
            throw new ObjectDisposedException(channelName);
        }

        throw new InvalidOperationException(
            $"{channelName} only supports one pending receive operation at a time.");
    }

    // Exact-prefix stream connections do not share a pooled buffer with disposal, so they retain
    // the cheaper active/idle flag instead of participating in the packed lifecycle below.
    public static void Exit(ref int receiveLifecycle) => Volatile.Write(ref receiveLifecycle, Idle);

    public static bool IsActive(int receiveLifecycle) => (receiveLifecycle & Active) != 0;

    public static void Exit(
        ref int receiveLifecycle,
        ref StreamFrameReceiveBuffer receiveBuffer)
    {
        if (!receiveBuffer.HasBuffer)
        {
            Exit(ref receiveLifecycle);
            return;
        }

        ExitWithPooledBuffer(ref receiveLifecycle, ref receiveBuffer);
    }

    private static void ExitWithPooledBuffer(
        ref int receiveLifecycle,
        ref StreamFrameReceiveBuffer receiveBuffer)
    {
        // Buffer access must finish before publishing Idle. Once Idle is visible, a successor may
        // enter and take ownership of this same buffer field.
        receiveBuffer.ReturnPooledBufferIfEmpty();
        if (!receiveBuffer.HasBuffer)
        {
            // Never touch the buffer after publishing idle: a successor may safely enter.
            Exit(ref receiveLifecycle);
            return;
        }

        var observed = Interlocked.CompareExchange(ref receiveLifecycle, Idle, Active);
        if (observed == Active)
        {
            return;
        }

        if (observed != DisposedActive)
        {
            throw new InvalidOperationException("Receive lifecycle ownership was lost before exit.");
        }

        // Disposal won the Active -> DisposedActive transition. No successor can enter, so this
        // receiver still owns final buffer reclamation before it publishes Disposed/Idle.
        receiveBuffer.ReturnPooledBuffer();
        Interlocked.Exchange(ref receiveLifecycle, Disposed);
    }

    public static bool TryPublishDisposedAndReleaseBufferIfIdle(
        ref int disposed,
        ref int receiveLifecycle,
        bool hasPooledBuffer,
        ref StreamFrameReceiveBuffer receiveBuffer)
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return false;
        }

        if (!hasPooledBuffer)
        {
            return true;
        }

        while (true)
        {
            var observed = Volatile.Read(ref receiveLifecycle);
            var disposedState = observed | Disposed;
            if (Interlocked.CompareExchange(
                    ref receiveLifecycle,
                    disposedState,
                    observed) != observed)
            {
                continue;
            }

            // Idle -> Disposed gives disposal exclusive buffer ownership. Active ->
            // DisposedActive transfers final reclamation to the active receiver.
            if (observed == Idle)
            {
                receiveBuffer.ReturnPooledBuffer();
            }

            return true;
        }
    }
}
