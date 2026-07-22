using System.IO.Pipes;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Transport;

internal static class StreamConnectionFrameSender
{
    public static async ValueTask SendAsync(
        StreamConnection connection,
        PooledBufferWriter frame,
        CancellationToken cancellationToken)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        try
        {
            var data = frame.WrittenMemory;
            connection.ThrowIfDisposedForSend();
            cancellationToken.ThrowIfCancellationRequested();
            MessageFramer.ValidateOutgoingFrame(data.Span, connection.MaxOutgoingMessageSize);

            await TransportSendGate.WaitAsync(
                connection.SendGate,
                connection.SendDisposalToken,
                cancellationToken,
                nameof(StreamConnection)).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.ThrowIfDisposedForSend();
                await connection.SendStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                if (connection.SendStream is not PipeStream)
                {
                    await connection.SendStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    connection.SendGate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Close may dispose the gate; any I/O fault already propagates from WriteAsync.
                }
            }
        }
        finally
        {
            frame.Dispose();
        }
    }
}
