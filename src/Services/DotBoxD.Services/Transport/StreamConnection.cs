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
    private readonly string _remoteEndpoint;
    private readonly int _maxMessageSize;
    private readonly TimeSpan _frameReadIdleTimeout;
    private readonly FrameReadTimeoutSource? _frameReadTimeout;
    private readonly byte[] _lengthBuffer = new byte[StreamFrameReadOperations.LengthPrefixSize];

    // Frames are atomic, so a started write or flush ignores the caller's token and observes only this
    // connection-lifetime token, which close cancels. A borrowed stream is not disposed on close, so this
    // is the only way to end a stalled write there. The token is cached because sends may still read it
    // after close has disposed the source.
    private readonly CancellationTokenSource _writeLifetime = new();
    private readonly CancellationToken _writeLifetimeToken;
    private StreamFrameReceiveBuffer _receiveBuffer;
    private Task? _closeTask;
    private int _activeReceives;
    private int _disposed;

    /// <summary>
    /// Creates a framed connection over <paramref name="stream"/>. A null timeout uses the finite
    /// default; <see cref="Timeout.InfiniteTimeSpan"/> disables it for trusted streams.
    /// The message size limit must be between <see cref="MessageFramer.HeaderSize"/> and
    /// <see cref="MessageFramer.MaxMessageSize"/> bytes, inclusive.
    /// </summary>
    public StreamConnection(
        Stream stream,
        string? remoteEndpoint = null,
        bool ownsStream = true,
        int maxMessageSize = MessageFramer.MaxMessageSize,
        TimeSpan? frameReadIdleTimeout = null)
    {
        if (maxMessageSize < MessageFramer.HeaderSize || maxMessageSize > MessageFramer.MaxMessageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMessageSize),
                maxMessageSize,
                $"Maximum message size must be between {MessageFramer.HeaderSize} and {MessageFramer.MaxMessageSize} bytes.");
        }

        var timeout = FrameReadTimeoutSource.Resolve(frameReadIdleTimeout, nameof(frameReadIdleTimeout));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = ownsStream;
        _useReceiveLookahead = ownsStream && (stream is PipeStream || stream is IStreamReceiveLookaheadCapable);
        _remoteEndpoint = remoteEndpoint ?? "stream";
        _maxMessageSize = maxMessageSize;
        _frameReadIdleTimeout = timeout;
        _frameReadTimeout = timeout == Timeout.InfiniteTimeSpan ? null : new FrameReadTimeoutSource();
        _writeLifetimeToken = _writeLifetime.Token;
    }

    /// <summary>Configured idle timeout for frame reads.</summary>
    internal TimeSpan FrameReadIdleTimeout => _frameReadIdleTimeout;
    internal Stream SendStream => _stream;
    internal SemaphoreSlim SendGate => _sendLock;
    internal int MaxOutgoingMessageSize => _maxMessageSize;

    /// <summary>Cancelled only when the connection closes; started writes and flushes observe it.</summary>
    internal CancellationToken WriteLifetimeToken => _writeLifetimeToken;
    internal void ThrowIfDisposedForSend() => ThrowIfDisposed();
    internal void ReleaseSendGate() =>
        TransportSendGate.ReleaseAfterSend(_sendLock, ref _disposed);

    // Publishes disposal synchronously (the close itself completes in the background), so the next
    // sender observes it before the failed sender releases the gate.
    internal void AbortAfterFailedWrite() => _ = StartClose();

    internal bool OwnsStream => _ownsStream;
    internal bool HasActiveReceive => Volatile.Read(ref _activeReceives) != 0;

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

        await TransportSendGate.WaitAsync(_sendLock, ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            try
            {
                // Frames are atomic: the caller's token covers only the gate wait, so cancelling one
                // call never truncates a frame and tears down the shared connection. Only close
                // (through the lifetime token) or an I/O failure ends a started write.
                await _stream.WriteAsync(data, _writeLifetimeToken).ConfigureAwait(false);
                if (_stream is not PipeStream)
                {
                    await _stream.FlushAsync(_writeLifetimeToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // A failed or close-cancelled write may have put part of the frame on the stream; never
                // let a later frame follow a truncated one.
                AbortAfterFailedWrite();
                throw;
            }
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (SemaphoreFullException) when (Volatile.Read(ref _disposed) != 0)
            {
                // Close supplied the terminal permit while this send still owned the original one.
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
        return StreamFrameReceiveOperation.Start(this, ct);
    }

    internal Stream FrameReceiveStream => _stream;
    internal bool UseFrameReceiveLookahead => _useReceiveLookahead;
    internal int MaxIncomingFrameSize => _maxMessageSize;
    internal byte[] FrameReceiveLengthBuffer => _lengthBuffer;
    internal FrameReadTimeoutSource? FrameReceiveTimeout => _frameReadTimeout;
    internal ref StreamFrameReceiveBuffer FrameReceiveBuffer => ref _receiveBuffer;
    internal void ThrowIfDisposedForReceive() => ThrowIfDisposed();
    internal bool HasDedicatedReceiveOperation =>
        StreamFrameReceiveOperationAcquisition.HasDedicatedOperation(this);
    internal bool HasDedicatedReceiveCache =>
        StreamFrameReceiveOperationAcquisition.HasDedicatedCache(this);
    internal int DedicatedReceiveOperationCount =>
        StreamFrameReceiveOperationAcquisition.GetDedicatedOperationCount(this);
    internal int AvailableDedicatedReceiveOperationCount =>
        StreamFrameReceiveOperationAcquisition.GetAvailableDedicatedOperationCount(this);
    internal bool IsDisposedForReceive => Volatile.Read(ref _disposed) != 0;

    internal void FinishFrameReceive(ref FrameReceiveOperationState state)
    {
        var writerBacked = state.WriterBacked;
        try
        {
            state.Owner.Dispose(writerBacked);
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

    public ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default) =>
        StreamConnectionFrameSender.SendAsync(this, frame, ct);
    public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default) =>
        RpcFramePayloadAdapter.DetachAsync(StartReceive(writerBacked: false, ct));

    /// <summary>Closes the connection. This operation is idempotent.</summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await TransportTaskWaiter.WaitAsync(StartClose(), ct).ConfigureAwait(false);
    }

    private Task StartClose()
    {
        Task closeTask;
        // Semaphore operations do not take the object's CLR monitor, so the private send gate can
        // also serialize one-time close publication without another per-connection allocation.
        lock (_sendLock)
        {
            if (_closeTask is null)
            {
                ReceiveConcurrencyGuard.TryPublishDisposedAndReleaseBufferIfIdle(
                    ref _disposed,
                    ref _activeReceives,
                    _useReceiveLookahead,
                    ref _receiveBuffer);
                _closeTask = Task.Run(CloseCoreAsync);
                ObserveFault(_closeTask);
            }

            closeTask = _closeTask;
        }

        return closeTask;
    }

    private async Task CloseCoreAsync()
    {
        TransportSendGate.WakeDisposedWaiters(_sendLock);
        StreamFrameReceiveOperationAcquisition.Remove(this);
        _frameReadTimeout?.Dispose();
        try
        {
            CancelStartedWrites();
        }
        finally
        {
            if (_ownsStream)
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    // Disposed state is already published, so no new write starts after this. A sender that raced past
    // its disposed check holds the cached, already-cancelled token and fails without touching the source.
    private void CancelStartedWrites()
    {
        try
        {
            _writeLifetime.Cancel();
        }
        finally
        {
            _writeLifetime.Dispose();
        }
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    public ValueTask DisposeAsync() => new(CloseAsync());

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(StreamConnection));
        }
    }
}
