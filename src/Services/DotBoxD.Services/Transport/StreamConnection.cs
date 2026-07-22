using System.Buffers.Binary;
using System.IO.Pipes;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Transport;

/// <summary>DotBoxD connection over a duplex stream, including named pipe streams.</summary>
public sealed class StreamConnection : IValidatedSerialFrameChannel
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _closeSync = new();
    private readonly string _remoteEndpoint;
    private readonly int _maxMessageSize;
    private readonly TimeSpan _frameReadIdleTimeout;
    private readonly FrameReadTimeoutSource? _frameReadTimeout;
    private readonly byte[] _lengthBuffer = new byte[StreamFrameReadOperations.LengthPrefixSize];
    private Task? _closeTask;
    private int _activeReceives;
    private int _disposed;

    /// <summary>
    /// Creates a framed connection over <paramref name="stream"/>. A null timeout uses the finite
    /// default; <see cref="Timeout.InfiniteTimeSpan"/> disables it for trusted streams.
    /// </summary>
    public StreamConnection(
        Stream stream,
        string? remoteEndpoint = null,
        bool ownsStream = true,
        int maxMessageSize = MessageFramer.MaxMessageSize,
        TimeSpan? frameReadIdleTimeout = null)
    {
        if (maxMessageSize < MessageFramer.HeaderSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMessageSize),
                maxMessageSize,
                "Maximum message size must be at least the DotBoxD header size.");
        }

        var timeout = FrameReadTimeoutSource.Resolve(frameReadIdleTimeout, nameof(frameReadIdleTimeout));

        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = ownsStream;
        _remoteEndpoint = remoteEndpoint ?? "stream";
        _maxMessageSize = maxMessageSize;
        _frameReadIdleTimeout = timeout;
        _frameReadTimeout = timeout == Timeout.InfiniteTimeSpan ? null : new FrameReadTimeoutSource();
    }

    /// <summary>Configured idle timeout for frame reads.</summary>
    internal TimeSpan FrameReadIdleTimeout => _frameReadIdleTimeout;

    public bool IsConnected =>
        Volatile.Read(ref _disposed) == 0 &&
        (_stream is not PipeStream pipe || pipe.IsConnected);

    public string RemoteEndpoint => _remoteEndpoint;

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        SendValueAsync(data, ct).AsTask();

    public async ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        MessageFramer.ValidateOutgoingFrame(data.Span, _maxMessageSize);

        await WaitForSendSlotAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
            if (_stream is not PipeStream)
            {
                await _stream.FlushAsync(ct).ConfigureAwait(false);
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
                // Close may dispose the gate; any I/O fault already propagates from WriteAsync.
            }
        }
    }

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        var frame = await ReceiveFrameValueAsync(ct).ConfigureAwait(false);
        return frame.DetachPayload();
    }

    public async ValueTask<RpcFrame> ReceiveFrameValueAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ReceiveConcurrencyGuard.Enter(ref _activeReceives, nameof(StreamConnection));
        Payload? payload = null;
        try
        {
            ThrowIfDisposed();

            var readToken = _frameReadTimeout?.Start(ct, _frameReadIdleTimeout) ?? ct;
            var remaining = StreamFrameReadOperations.LengthPrefixSize;

            while (true)
            {
                while (remaining > 0)
                {
                    int read;
                    try
                    {
                        read = await _stream.ReadAsync(
                            payload is null
                                ? _lengthBuffer.AsMemory(
                                    StreamFrameReadOperations.LengthPrefixSize - remaining,
                                    remaining)
                                : payload.Memory.Slice(
                                    payload.Length - remaining,
                                    remaining),
                            readToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (
                        StreamFrameReadOperations.IsTimeoutCancellation(_frameReadTimeout))
                    {
                        throw FrameReadTimeoutSource.CreateTimeoutException(_frameReadIdleTimeout);
                    }

                    if (read == 0)
                    {
                        return StreamFrameReadOperations.HandleEndOfStream(
                            payload,
                            remaining,
                            StreamFrameReadProgressFormat.WholeFrame);
                    }

                    remaining -= read;
                    if (remaining > 0 && _frameReadTimeout is not null)
                    {
                        readToken = _frameReadTimeout.Rearm(_frameReadIdleTimeout);
                    }
                }

                if (payload is not null)
                {
                    var frame = new RpcFrame(payload);
                    payload = null;
                    return frame;
                }

                var totalLength = BinaryPrimitives.ReadInt32LittleEndian(_lengthBuffer);
                MessageFrameReader.ValidateIncomingFrameLength(totalLength, _maxMessageSize);

                payload = Payload.Rent(totalLength);
                _lengthBuffer.CopyTo(payload.Memory.Span);
                remaining = totalLength - StreamFrameReadOperations.LengthPrefixSize;
                readToken = _frameReadTimeout?.Start(ct, _frameReadIdleTimeout) ?? ct;
            }
        }
        finally
        {
            try
            {
                payload?.Dispose();
            }
            finally
            {
                try
                {
                    _frameReadTimeout?.CancelPendingTimeout();
                }
                finally
                {
                    ReceiveConcurrencyGuard.Exit(ref _activeReceives);
                }
            }
        }
    }

    public async ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        try
        {
            await SendValueAsync(frame.WrittenMemory, ct).ConfigureAwait(false);
        }
        finally
        {
            frame.Dispose();
        }
    }

    public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default) =>
        RpcFramePayloadAdapter.DetachAsync(ReceiveFrameValueAsync(ct));

    /// <summary>Closes the connection. This operation is idempotent.</summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        Task closeTask;
        lock (_closeSync)
        {
            if (_closeTask is null)
            {
                Volatile.Write(ref _disposed, 1);
                _closeTask = Task.Run(CloseCoreAsync);
            }

            closeTask = _closeTask;
        }

        await TransportTaskWaiter.WaitAsync(closeTask, ct).ConfigureAwait(false);
    }

    private async Task CloseCoreAsync()
    {
        _disposeCts.Cancel();
        _frameReadTimeout?.Dispose();
        if (_ownsStream || Volatile.Read(ref _activeReceives) != 0)
        {
            await DisposeStreamAsync(_stream).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(StreamConnection));
        }
    }

    private async Task WaitForSendSlotAsync(CancellationToken ct)
    {
        try
        {
            if (_sendLock.Wait(0))
            {
                if (ct.IsCancellationRequested)
                {
                    _sendLock.Release();
                    ct.ThrowIfCancellationRequested();
                }

                return;
            }

            if (ct.CanBeCanceled)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
                await _sendLock.WaitAsync(linked.Token).ConfigureAwait(false);
                return;
            }

            await _sendLock.WaitAsync(_disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested &&
            Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(StreamConnection));
        }
    }

    private static async ValueTask DisposeStreamAsync(Stream stream)
    {
        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Closing is best-effort.
        }
    }
}
