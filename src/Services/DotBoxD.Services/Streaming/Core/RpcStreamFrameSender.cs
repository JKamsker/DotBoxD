using System.Runtime.ExceptionServices;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Streaming.Core;

internal readonly struct RpcStreamFrameSender
{
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly Func<PooledBufferWriter, CancellationToken, ValueTask>? _sendFrameAsync;

    public RpcStreamFrameSender(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Func<PooledBufferWriter, CancellationToken, ValueTask>? sendFrameAsync)
    {
        _sendAsync = sendAsync;
        _sendFrameAsync = sendFrameAsync;
    }

    public RpcStreamFrameSender(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        ValidatedOwnedFrameSender sendFrameSender)
        : this(sendAsync, sendFrameSender.SendAsync)
    {
    }

    public ValueTask SendAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        try
        {
            MessageFramer.ValidateOutgoingFrame(frame.WrittenSpan);
        }
        catch (Exception ex)
        {
            return DisposeAndCapture(frame, ex);
        }

        if (_sendFrameAsync is not null)
        {
            try
            {
                return _sendFrameAsync(frame, ct);
            }
            catch (Exception ex)
            {
                return DisposeAndCapture(frame, ex);
            }
        }

        return SendMemoryAsync(_sendAsync, frame, ct);
    }

    private static async ValueTask SendMemoryAsync(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        PooledBufferWriter frame,
        CancellationToken cancellationToken)
    {
        try
        {
            await sendAsync(frame.WrittenMemory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            frame.Dispose();
        }
    }

    private static ValueTask DisposeAndCapture(PooledBufferWriter frame, Exception exception)
    {
        try
        {
            frame.Dispose();
        }
        catch (Exception disposeException)
        {
            return CaptureExceptionAsync(disposeException);
        }

        return CaptureExceptionAsync(exception);
    }

    private static async ValueTask CaptureExceptionAsync(Exception exception)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
