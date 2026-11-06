using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>Releases owned-frame send resources before producer completion is published.</summary>
internal static class StreamFrameSendCleanup
{
    /// <summary>Cleans up the send and returns the exception to publish, or null on success.</summary>
    public static Exception? Complete(ref StreamFrameSendState state, Exception? error)
    {
        var connection = state.Connection;
        var frame = state.Frame;
        var gateHeld = state.GateHeld;
        var callerToken = state.CallerToken;
        var writeStarted = state.Stage is StreamFrameSendStage.Write or StreamFrameSendStage.Flush;
        state = default;

        return Complete(connection, frame, gateHeld, error, writeStarted, callerToken);
    }

    /// <summary>Cleans up the send and returns the exception to publish, or null on success.</summary>
    public static Exception? Complete(
        StreamConnection? connection,
        PooledBufferWriter? frame,
        bool gateHeld,
        Exception? error,
        bool writeStarted,
        CancellationToken callerToken)
    {
        var startedWriteFailed = error is not null && writeStarted;
        var cleanupError = Finish(connection, frame, gateHeld, abortConnection: startedWriteFailed);
        if (cleanupError is not null)
        {
            return cleanupError;
        }

        return startedWriteFailed && connection is not null
            ? StreamFrameSendFailure.TranslateStartedWrite(connection, error!, callerToken)
            : error;
    }

    private static Exception? Finish(
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
}
