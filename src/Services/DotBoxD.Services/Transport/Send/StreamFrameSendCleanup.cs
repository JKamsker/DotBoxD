using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>Releases owned-frame send resources before producer completion is published.</summary>
internal static class StreamFrameSendCleanup
{
    public static Exception? Finish(ref StreamFrameSendState state)
    {
        var connection = state.Connection;
        var frame = state.Frame;
        var gateHeld = state.GateHeld;
        state = default;

        return Finish(connection, frame, gateHeld);
    }

    public static Exception? Finish(
        StreamConnection? connection,
        PooledBufferWriter? frame,
        bool gateHeld)
    {
        Exception? cleanupError = null;
        try
        {
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

    public static void FinishOrThrow(ref StreamFrameSendState state)
    {
        if (Finish(ref state) is { } error)
        {
            throw error;
        }
    }
}
