using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>Starts an owned-frame send without allocating for synchronously completed stages.</summary>
internal static class StreamConnectionFrameSender
{
    public static ValueTask SendAsync(
        StreamConnection connection,
        PooledBufferWriter frame,
        CancellationToken cancellationToken)
    {
        if (frame is null)
        {
            return StreamFrameSendFailure.Create(
                new ArgumentNullException(nameof(frame)));
        }

        var state = new StreamFrameSendState();
        ValueTask pendingOperation;
        bool completed;
        try
        {
            StreamFrameSendDriver.Initialize(connection, frame, cancellationToken, ref state);
            completed = StreamFrameSendDriver.TryAdvance(ref state, out pendingOperation);
        }
        catch (Exception error)
        {
            return CompleteSynchronously(ref state, error);
        }

        if (completed)
        {
            return CompleteSynchronously(ref state, error: null);
        }

        StreamFrameSendOperation? operation;
        try
        {
            operation = StreamFrameSendOperation.TryRentOrCreate();
        }
        catch
        {
            // Pooling is only an optimization. The transport operation is already live and still
            // owns frame memory, so it must drain through the ownership-preserving fallback.
            operation = null;
        }

        if (operation is null)
        {
            var fallback = StreamFrameSendFallback.Start(state, pendingOperation);
            state = default;
            return fallback;
        }

        return operation.Start(ref state, pendingOperation);
    }

    private static ValueTask CompleteSynchronously(
        ref StreamFrameSendState state,
        Exception? error)
    {
        var cleanupError = StreamFrameSendCleanup.Finish(ref state);
        error = cleanupError ?? error;
        return error is null
            ? default
            : StreamFrameSendFailure.Create(error);
    }
}
