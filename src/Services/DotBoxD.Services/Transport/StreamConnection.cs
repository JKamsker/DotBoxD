using System.IO.Pipes;
using System.Runtime.CompilerServices;
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
    internal Stream SendStream => _stream;
    internal SemaphoreSlim SendGate => _sendLock;
    internal CancellationToken SendDisposalToken => _disposeCts.Token;
    internal int MaxOutgoingMessageSize => _maxMessageSize;
    internal void ThrowIfDisposedForSend() => ThrowIfDisposed();

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
        var frame = await StartReceive(writerBacked: false, ct).ConfigureAwait(false);
        return frame.DetachPayload();
    }

    public ValueTask<RpcFrame> ReceiveFrameValueAsync(CancellationToken ct = default) =>
        StartReceive(writerBacked: true, ct);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask<RpcFrame> StartReceive(bool writerBacked, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return StreamFrameReadOperations.CreateCanceledReceive(ct);
        }

        var failure = ReceiveConcurrencyGuard.TryEnter(ref _activeReceives);
        if (failure != ReceiveEnterFailure.None)
        {
            return StreamFrameReadOperations.CreateFailedReceive(failure, nameof(StreamConnection));
        }

        _receiveBuffer.WriterBackedOwner = writerBacked;
        return ReceiveFrameCoreAsync(ct);
    }

    private async ValueTask<RpcFrame> ReceiveFrameCoreAsync(CancellationToken ct)
    {
        var owner = new StreamFrameReceiveOwner();
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
                                    ref owner,
                                    remaining),
                                readToken);
                            StreamFrameReadOperations.ObservePendingRead(
                                ref _receiveBuffer,
                                owner,
                                pendingRead.IsCompletedSuccessfully);
                        }
                        else
                        {
                            pendingRead = _stream.ReadAsync(
                                StreamFrameReadOperations.PrepareExactRead(
                                    _lengthBuffer,
                                    ref owner,
                                    _receiveBuffer.WriterBackedOwner,
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
                            owner,
                            remaining,
                            StreamFrameReadProgressFormat.WholeFrame);
                    }
                    if (_useReceiveLookahead)
                    {
                        remaining = StreamFrameReadOperations.CommitRead(
                            ref _receiveBuffer,
                            ref owner,
                            remaining,
                            read);
                    }
                    else
                    {
                        owner.AdvanceBodyRead(read, _receiveBuffer.WriterBackedOwner);
                        remaining -= read;
                    }
                    readToken = StreamFrameReadOperations.RearmTimeout(
                        _frameReadTimeout,
                        readToken,
                        _frameReadIdleTimeout,
                        remaining);
                }
                if (owner.IsAllocated)
                {
                    return owner.TransferFrame(_receiveBuffer.WriterBackedOwner);
                }
                remaining = _useReceiveLookahead
                    ? StreamFrameReadOperations.InitializeOwner(
                        ref _receiveBuffer,
                        _lengthBuffer,
                        _maxMessageSize,
                        _receiveBuffer.WriterBackedOwner,
                        ref owner)
                    : StreamFrameReadOperations.InitializeExactOwner(
                        _lengthBuffer,
                        _maxMessageSize,
                        _receiveBuffer.WriterBackedOwner,
                        ref owner);
            }
        }
        finally
        {
            try
            {
                owner.Dispose(_receiveBuffer.WriterBackedOwner);
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

    public ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default) =>
        StreamConnectionFrameSender.SendAsync(this, frame, ct);
    public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default) =>
        RpcFramePayloadAdapter.DetachAsync(StartReceive(writerBacked: false, ct));

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
