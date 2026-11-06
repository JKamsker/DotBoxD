using System.IO.Pipes;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

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

        var gateHeld = false;
        var writeStarted = false;
        var pendingHandoffStarted = false;
        try
        {
            var data = frame.WrittenMemory;
            connection.ThrowIfDisposedForSend();
            cancellationToken.ThrowIfCancellationRequested();
            MessageFramer.ValidateOutgoingFrame(data.Span, connection.MaxOutgoingMessageSize);

            var gateWait = TransportSendGate.WaitAsync(
                connection.SendGate,
                cancellationToken);
            if (!gateWait.IsCompleted)
            {
                pendingHandoffStarted = true;
                return ContinuePending(
                    connection,
                    frame,
                    data,
                    cancellationToken,
                    StreamFrameSendStage.AcquireGate,
                    gateHeld: false,
                    gateWait);
            }

            gateWait.GetAwaiter().GetResult();
            gateHeld = true;
            cancellationToken.ThrowIfCancellationRequested();
            connection.ThrowIfDisposedForSend();

            // Frames are atomic: the caller's token covers only the gate wait (see StreamFrameSendDriver).
            writeStarted = true;
            var write = connection.SendStream.WriteAsync(data, connection.WriteLifetimeToken);
            if (!write.IsCompleted)
            {
                pendingHandoffStarted = true;
                return ContinuePending(
                    connection,
                    frame,
                    data,
                    cancellationToken,
                    StreamFrameSendStage.Write,
                    gateHeld,
                    write);
            }

            write.GetAwaiter().GetResult();
            if (connection.SendStream is not PipeStream)
            {
                var flush = connection.SendStream.FlushAsync(connection.WriteLifetimeToken);
                if (!flush.IsCompleted)
                {
                    pendingHandoffStarted = true;
                    return ContinuePending(
                        connection,
                        frame,
                        data,
                        cancellationToken,
                        StreamFrameSendStage.Flush,
                        gateHeld,
                        new ValueTask(flush));
                }

                flush.GetAwaiter().GetResult();
            }
        }
        catch (Exception error) when (!pendingHandoffStarted)
        {
            return CompleteSynchronously(connection, frame, gateHeld, error, writeStarted, cancellationToken);
        }

        return CompleteSynchronously(connection, frame, gateHeld, error: null, writeStarted, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask ContinuePending(
        StreamConnection connection,
        PooledBufferWriter frame,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken,
        StreamFrameSendStage stage,
        bool gateHeld,
        ValueTask pendingOperation)
    {
        var state = new StreamFrameSendState
        {
            Connection = connection,
            Frame = frame,
            Data = data,
            CallerToken = cancellationToken,
            Stage = stage,
            GateHeld = gateHeld,
        };

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
        StreamConnection connection,
        PooledBufferWriter frame,
        bool gateHeld,
        Exception? error,
        bool writeStarted,
        CancellationToken cancellationToken)
    {
        error = StreamFrameSendCleanup.Complete(
            connection,
            frame,
            gateHeld,
            error,
            writeStarted,
            cancellationToken);
        return error is null
            ? default
            : StreamFrameSendFailure.Create(error);
    }
}
