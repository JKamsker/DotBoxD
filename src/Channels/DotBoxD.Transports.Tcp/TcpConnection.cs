using System.Net.Sockets;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>TCP-based connection implementation.</summary>
public sealed class TcpConnection : IValidatedSerialFrameChannel
{
    /// <summary>Default idle timeout applied to frame reads (30 seconds).</summary>
    public static readonly TimeSpan DefaultFrameReadIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly TimeSpan _frameReadIdleTimeout;
    private readonly FrameReadTimeoutSource? _frameReadTimeout;
    private readonly byte[] _lengthBuffer = new byte[StreamFrameReadOperations.LengthPrefixSize];
    private StreamFrameReceiveBuffer _receiveBuffer;
    private int _activeReceive;
    private int _disposed;

    public TcpConnection(TcpClient client) : this(client, null)
    {
    }

    /// <summary>
    /// Creates a TCP connection with an idle bound for both first-byte and body reads. Pass
    /// <see cref="Timeout.InfiniteTimeSpan"/> to disable; null uses the default.
    /// </summary>
    public TcpConnection(TcpClient client, TimeSpan? frameReadIdleTimeout)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        var timeout = FrameReadTimeoutSource.Resolve(
            frameReadIdleTimeout,
            DefaultFrameReadIdleTimeout,
            nameof(frameReadIdleTimeout));
        _frameReadIdleTimeout = timeout;
        _frameReadTimeout = timeout == Timeout.InfiniteTimeSpan ? null : new FrameReadTimeoutSource();
        _client.NoDelay = true;
        _stream = client.GetStream();
        // Capture the endpoint once: after DisposeAsync closes the client its underlying socket is
        // disposed, so reading RemoteEndPoint live would throw ObjectDisposedException from logging
        // or a Disconnected handler. Mirrors StreamConnection's cached endpoint.
        RemoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Best-effort liveness hint combining disposal with the socket's last known state. This does
    /// not probe the wire; rely on I/O exceptions for authoritative connection state.
    /// </summary>
    public bool IsConnected => _client.Connected && Volatile.Read(ref _disposed) == 0;
    public string RemoteEndpoint { get; }
    internal NetworkStream SendStream => _stream;
    internal SemaphoreSlim SendGate => _sendLock;
    internal void ThrowIfDisposedForSend() => ThrowIfDisposed();
    internal void ReleaseSendGate() => TransportSendGate.ReleaseAfterSend(_sendLock, ref _disposed);

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        SendValueAsync(data, ct).AsTask();

    public async ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        // Reject malformed/oversized frames locally rather than shipping them to the peer, matching
        // StreamConnection and the inbound length check in ReceiveAsync below.
        MessageFramer.ValidateOutgoingFrame(data.Span);

        await TransportSendGate.WaitAsync(_sendLock, ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        }
        finally
        {
            TransportSendGate.ReleaseAfterSend(_sendLock, ref _disposed);
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
        if (Volatile.Read(ref _disposed) != 0)
        {
            return StreamFrameReadOperations.CreateFailedReceive(
                new ObjectDisposedException(nameof(TcpConnection)));
        }

        var failure = ReceiveConcurrencyGuard.TryEnter(ref _activeReceive);
        if (failure != ReceiveEnterFailure.None)
        {
            return StreamFrameReadOperations.CreateFailedReceive(failure, nameof(TcpConnection));
        }

        _receiveBuffer.WriterBackedOwner = writerBacked;
        return TcpFrameReceiveOperation.Start(this, ct);
    }

    internal NetworkStream FrameReceiveStream => _stream;
    internal TimeSpan FrameReadIdleTimeout => _frameReadIdleTimeout;
    internal byte[] FrameReceiveLengthBuffer => _lengthBuffer;
    internal FrameReadTimeoutSource? FrameReceiveTimeout => _frameReadTimeout;
    internal ref StreamFrameReceiveBuffer FrameReceiveBuffer => ref _receiveBuffer;
    internal void ThrowIfDisposedForReceive() => ThrowIfDisposed();

    internal void FinishFrameReceive(ref FrameReceiveOperationState state)
    {
        FinishFrameReceive(ref state.Owner, state.WriterBacked);
    }

    internal void FinishFrameReceive(ref StreamFrameReceiveOwner owner, bool writerBacked)
    {
        try
        {
            owner.Dispose(writerBacked);
        }
        finally
        {
            try
            {
                _frameReadTimeout?.CancelPendingTimeout();
            }
            finally
            {
                ReceiveConcurrencyGuard.Exit(ref _activeReceive, ref _receiveBuffer);
            }
        }
    }

    public ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default) =>
        TcpConnectionFrameSender.SendAsync(this, frame, ct);

    public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default)
    {
        var pending = StartReceive(writerBacked: false, ct);
        if (pending.IsCompletedSuccessfully)
        {
            var frame = pending.Result;
            return new ValueTask<Payload>(frame.DetachPayload());
        }

        return AwaitPayloadAsync(pending);
    }

    private static async ValueTask<Payload> AwaitPayloadAsync(ValueTask<RpcFrame> pending)
    {
        var frame = await pending.ConfigureAwait(false);
        return frame.DetachPayload();
    }

    public ValueTask DisposeAsync()
    {
        if (!ReceiveConcurrencyGuard.TryPublishDisposedAndReleaseBufferIfIdle(
                ref _disposed,
                ref _activeReceive,
                hasPooledBuffer: true,
                ref _receiveBuffer))
        {
            return default;
        }

        TransportSendGate.WakeDisposedWaiters(_sendLock);
        _frameReadTimeout?.Dispose();
        try
        {
            _stream.Close();
            _client.Close();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        return default;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
        }
    }

}
