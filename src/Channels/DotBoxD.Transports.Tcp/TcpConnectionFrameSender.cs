using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

internal static class TcpConnectionFrameSender
{
    public static async ValueTask SendAsync(
        TcpConnection connection,
        PooledBufferWriter frame,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = frame.WrittenMemory;
            connection.ThrowIfDisposedForSend();
            cancellationToken.ThrowIfCancellationRequested();
            MessageFramer.ValidateOutgoingFrame(data.Span);

            await TransportSendGate.WaitAsync(
                connection.SendGate,
                cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.ThrowIfDisposedForSend();
                await connection.SendStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                connection.ReleaseSendGate();
            }
        }
        finally
        {
            frame.Dispose();
        }
    }
}
