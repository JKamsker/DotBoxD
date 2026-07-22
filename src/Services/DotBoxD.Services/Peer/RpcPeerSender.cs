using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Peer;

internal sealed class RpcPeerSender : IDisposable
{
    private readonly IRpcChannel _channel;
    private readonly IRpcValueTaskChannel? _valueTaskChannel;
    private readonly IRpcFrameChannel? _frameChannel;
    private readonly IValidatedSerialFrameChannel? _validatedSerialFrameChannel;
    private readonly Func<bool> _isClosed;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public RpcPeerSender(IRpcChannel channel, Func<bool> isClosed)
    {
        _channel = channel;
        _valueTaskChannel = channel as IRpcValueTaskChannel;
        _frameChannel = channel as IRpcFrameChannel;
        _validatedSerialFrameChannel = channel as IValidatedSerialFrameChannel;
        _isClosed = isClosed;
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var validatedSerialFrameChannel = _validatedSerialFrameChannel;
        if (validatedSerialFrameChannel is null)
        {
            return SendSlowAsync(data, ct);
        }

        if (_isClosed())
        {
            return Task.FromException(new ServiceConnectionException("Connection closed."));
        }

        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled(ct);
        }

        return validatedSerialFrameChannel.SendValueAsync(data, ct).AsTask();
    }

    private async Task SendSlowAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        // Fast-fail once the peer is closing so an outbound call started during teardown does not
        // park in WaitAsync (with a non-cancellable token) only to strand on a disposed send lock.
        if (_isClosed())
        {
            throw new ServiceConnectionException("Connection closed.");
        }

        try
        {
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // DisposeAsync disposed the send lock while this send raced teardown; surface the
            // connection contract rather than leaking ObjectDisposedException to the caller.
            throw new ServiceConnectionException("Connection closed.");
        }

        try
        {
            if (_isClosed())
            {
                throw new ServiceConnectionException("Connection closed.");
            }

            if (_valueTaskChannel is null)
            {
                await _channel.SendAsync(data, ct).ConfigureAwait(false);
            }
            else
            {
                await _valueTaskChannel.SendValueAsync(data, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        var validatedSerialFrameChannel = _validatedSerialFrameChannel;
        if (validatedSerialFrameChannel is not null)
        {
            if (_isClosed())
            {
                return ClosedFrame(frame);
            }

            return validatedSerialFrameChannel.SendFrameValueAsync(frame, ct);
        }

        return SendFrameSlowAsync(frame, ct);
    }

    private async ValueTask SendFrameSlowAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        if (_frameChannel is null)
        {
            try
            {
                await SendAsync(frame.WrittenMemory, ct).ConfigureAwait(false);
            }
            finally
            {
                frame.Dispose();
            }

            return;
        }

        if (_isClosed())
        {
            frame.Dispose();
            throw new ServiceConnectionException("Connection closed.");
        }

        try
        {
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            frame.Dispose();
            throw new ServiceConnectionException("Connection closed.");
        }
        catch
        {
            frame.Dispose();
            throw;
        }

        try
        {
            if (_isClosed())
            {
                throw new ServiceConnectionException("Connection closed.");
            }

            MessageFramer.ValidateOutgoingFrame(frame.WrittenSpan);
            await _frameChannel.SendFrameValueAsync(frame, ct).ConfigureAwait(false);
            frame = null!;
        }
        finally
        {
            frame?.Dispose();
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static ValueTask ClosedFrame(PooledBufferWriter frame)
    {
        frame.Dispose();
        return new ValueTask(Task.FromException(new ServiceConnectionException("Connection closed.")));
    }

    public void Dispose() => _sendLock.Dispose();
}
