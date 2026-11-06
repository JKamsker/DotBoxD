using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>Releases owned-frame send resources before producer completion is published.</summary>
internal static class StreamFrameSendCleanup
{
    public static Exception? Finish(ref StreamFrameSendState state, Exception? error)
    {
        var connection = state.Connection;
        var frame = state.Frame;
        var gateHeld = state.GateHeld;
        var writeStarted = state.Stage is StreamFrameSendStage.Write or StreamFrameSendStage.Flush;
        state = default;

        return Finish(connection, frame, gateHeld, abortConnection: error is not null && writeStarted);
    }

    public static Exception? Finish(
        StreamConnection? connection,
        PooledBufferWriter? frame,
        bool gateHeld,
        bool abortConnection)
    {
        Exception? cleanupError = null;
        try
        {
            if (abortConnection)
            {
                // A write failed after it started, so a partial frame may be on the stream. Poison the
                // connection before releasing the gate so no later frame is appended to a truncated one.
                connection?.AbortAfterFailedWrite();
            }

            if (gateHeld && connection is not null)
            {
                connection.ReleaseSendGate();
            }
        }
        catch (Exception error)
        {
            cleanupError = error;
        }
        finally
        {
            try
            {
                frame?.Dispose();
            }
            catch (Exception error)
            {
                cleanupError = error;
            }
        }

        return cleanupError;
    }

    public static void FinishOrThrow(ref StreamFrameSendState state, Exception? error)
    {
        if (Finish(ref state, error) is { } cleanupError)
        {
            throw cleanupError;
        }
    }
}
