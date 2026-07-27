namespace DotBoxD.Services.Transport;

/// <summary>Selects lookahead or exact reads for the scalar Stream receive fallback.</summary>
internal static class StreamFrameReceiveFallbackRead
{
    public static ValueTask<int> Start(
        StreamConnection connection,
        ref StreamFrameReceiveOwner owner,
        int remaining,
        CancellationToken readToken)
    {
        if (!connection.UseFrameReceiveLookahead)
        {
            return connection.FrameReceiveStream.ReadAsync(
                StreamFrameReadOperations.PrepareExactRead(
                    connection.FrameReceiveLengthBuffer,
                    ref owner,
                    connection.FrameReceiveBuffer.WriterBackedOwner,
                    remaining),
                readToken);
        }

        var pendingRead = connection.FrameReceiveStream.ReadAsync(
            StreamFrameReadOperations.PrepareRead(
                ref connection.FrameReceiveBuffer,
                connection.FrameReceiveLengthBuffer,
                ref owner,
                remaining),
            readToken);
        StreamFrameReadOperations.ObservePendingRead(
            ref connection.FrameReceiveBuffer,
            owner,
            pendingRead.IsCompletedSuccessfully);
        return pendingRead;
    }
}
