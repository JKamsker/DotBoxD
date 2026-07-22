using System.IO.Pipes;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Transport;

/// <summary>DotBoxD connection over a duplex stream, including named pipe streams.</summary>
public sealed class StreamConnection : IValidatedSerialFrameChannel
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly bool _useReceiveLookahead;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly string _remoteEndpoint;
    private readonly int _maxMessageSize;
    private readonly TimeSpan _frameReadIdleTimeout;
    private readonly FrameReadTimeoutSource? _frameReadTimeout;
    private readonly byte[] _lengthBuffer = new byte[StreamFrameReadOperations.LengthPrefixSize];
    private StreamFrameReceiveBuffer _receiveBuffer;
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
        _useReceiveLookahead = ownsStream && (stream is PipeStream || stream is IStreamReceiveLookaheadCapable);
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

        await TransportSendGate.WaitAsync(
            _sendLock,
            _disposeCts.Token,
            ct,
            nameof(StreamConnection)).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
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
            var readToken = ct;
            var remaining = _useReceiveLookahead
                ? StreamFrameReadOperations.BeginFrame(ref _receiveBuffer)
                : StreamFrameReadOperations.LengthPrefixSize;
            while (true)
            {
                readToken = StreamFrameReadOperations.StartTimeout(
                    _frameReadTimeout,
                    ct,
                    _frameReadIdleTimeout,
                    remaining);
                while (remaining > 0)
                {
                    int read;
                    try
                    {
                        ValueTask<int> pendingRead;
                        if (_useReceiveLookahead)
                        {
                            pendingRead = _stream.ReadAsync(
                                StreamFrameReadOperations.PrepareRead(
                                    ref _receiveBuffer,
                                    _lengthBuffer,
                                    payload,
                                    remaining),
                                readToken);
                            StreamFrameReadOperations.ObservePendingRead(
                                ref _receiveBuffer,
                                payload,
                                pendingRead.IsCompletedSuccessfully);
                        }
                        else
                        {
                            pendingRead = _stream.ReadAsync(
                                StreamFrameReadOperations.PrepareExactRead(
                                    _lengthBuffer,
                                    payload,
                                    remaining),
                                readToken);
                        }

                        read = await pendingRead.ConfigureAwait(false);
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
                    if (_useReceiveLookahead)
                    {
                        remaining = StreamFrameReadOperations.CommitRead(
                            ref _receiveBuffer,
                            payload,
                            remaining,
                            read);
                    }
                    else
                    {
                        remaining -= read;
                    }
                    readToken = StreamFrameReadOperations.RearmTimeout(
                        _frameReadTimeout,
                        readToken,
                        _frameReadIdleTimeout,
                        remaining);
                }
                if (payload is not null)
                {
                    var frame = new RpcFrame(payload);
                    payload = null;
                    return frame;
                }
                remaining = _useReceiveLookahead
                    ? StreamFrameReadOperations.InitializePayload(
                        ref _receiveBuffer,
                        _lengthBuffer,
                        _maxMessageSize,
                        out payload)
                    : StreamFrameReadOperations.InitializeExactPayload(
                        _lengthBuffer,
                        _maxMessageSize,
                        out payload);
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
                    ReceiveConcurrencyGuard.Exit(ref _activeReceives, ref _receiveBuffer);
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
        // The private CTS has connection lifetime and also serializes one-time close publication.
        lock (_disposeCts)
        {
            if (_closeTask is null)
            {
                ReceiveConcurrencyGuard.TryPublishDisposedAndReleaseBufferIfIdle(
                    ref _disposed,
                    ref _activeReceives,
                    _useReceiveLookahead,
                    ref _receiveBuffer);
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
        if (_ownsStream || ReceiveConcurrencyGuard.IsActive(Volatile.Read(ref _activeReceives)))
        {
            await StreamFrameReadOperations.DisposeBestEffortAsync(_stream).ConfigureAwait(false);
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
}
