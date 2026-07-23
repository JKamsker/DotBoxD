using DotBoxD.Services.Buffers;

namespace DotBoxD.Transports.Tcp;

internal static class TcpConnectionFrameSender
{
    public static ValueTask SendAsync(
        TcpConnection connection,
        PooledBufferWriter frame,
        CancellationToken cancellationToken)
    {
        if (frame is null)
        {
            return TcpFrameSendFallback.CreateFailure(
                new ArgumentNullException(nameof(frame)));
        }

        var state = default(TcpFrameSendState);
        ValueTask pendingOperation;
        try
        {
            TcpFrameSendDriver.Initialize(connection, frame, cancellationToken, ref state);
            if (TcpFrameSendDriver.TryAdvance(ref state, out pendingOperation))
            {
                return CompleteSynchronously(ref state);
            }
        }
        catch (Exception error)
        {
            return FailSynchronously(ref state, error);
        }

        // No synchronous-failure cleanup may run after a transport operation suspends: the
        // pending I/O can still retain the frame until one of these continuations finishes it.
        return StartPending(ref state, pendingOperation);
    }

    internal static ValueTask ContinuePendingWriteForTests(
        TcpConnection connection,
        PooledBufferWriter frame,
        CancellationToken cancellationToken,
        ValueTask pendingWrite)
    {
        var state = default(TcpFrameSendState);
        try
        {
            TcpFrameSendDriver.InitializePendingWriteForTests(
                connection,
                frame,
                cancellationToken,
                ref state);
        }
        catch (Exception error)
        {
            return FailSynchronously(ref state, error);
        }

        return StartPending(ref state, pendingWrite);
    }

    private static ValueTask StartPending(
        ref TcpFrameSendState state,
        ValueTask pendingOperation)
    {
        TcpFrameSendOperation? operation;
        try
        {
            operation = TcpFrameSendOperation.TryRentOrCreate();
        }
        catch
        {
            // Pooling is only an optimization. The pending transport operation may still retain
            // the owned frame, so transfer it to the task-backed continuation instead of cleaning
            // that ownership while I/O remains active.
            return TcpFrameSendFallback.Start(ref state, pendingOperation);
        }

        if (operation is null)
        {
            return TcpFrameSendFallback.Start(ref state, pendingOperation);
        }

        return operation.Start(ref state, pendingOperation);
    }

    private static ValueTask CompleteSynchronously(ref TcpFrameSendState state)
    {
        var cleanupError = TcpFrameSendDriver.FinishAndClear(ref state);
        return cleanupError is null
            ? default
            : TcpFrameSendFallback.CreateFailure(cleanupError);
    }

    private static ValueTask FailSynchronously(
        ref TcpFrameSendState state,
        Exception error)
    {
        var cleanupError = TcpFrameSendDriver.FinishAndClear(ref state);
        return TcpFrameSendFallback.CreateFailure(cleanupError ?? error);
    }
}
