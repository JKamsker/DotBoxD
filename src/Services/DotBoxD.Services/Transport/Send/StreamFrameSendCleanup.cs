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

        Exception? cleanupError = null;
        try
        {
            if (gateHeld && connection is not null)
            {
                try
                {
                    connection.SendGate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Close may dispose the gate; an earlier I/O failure remains authoritative.
                }
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
