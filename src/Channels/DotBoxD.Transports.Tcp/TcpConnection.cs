using System.Buffers.Binary;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>
/// TCP-based connection implementation.
/// </summary>
public sealed class TcpConnection : IValidatedSerialFrameChannel
{
    /// <summary>Default idle timeout applied to frame reads (30 seconds).</summary>
    public static readonly TimeSpan DefaultFrameReadIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly TimeSpan _frameReadIdleTimeout;
    private readonly FrameReadTimeoutSource? _frameReadTimeout;
    private readonly byte[] _lengthBuffer = new byte[StreamFrameReadOperations.LengthPrefixSize];
    private int _activeReceive;
    private int _disposed;

    public TcpConnection(TcpClient client) : this(client, null)
    {
    }

    /// <summary>
    /// Creates a TCP connection. <paramref name="frameReadIdleTimeout"/> bounds how long an
    /// frame read may stall with no data before the connection is torn down. This bounds both the first
    /// frame byte and body reads so a slow-loris peer cannot pin a connection or rented buffer. Pass
    /// <see cref="Timeout.InfiniteTimeSpan"/> to disable; <see langword="null"/> uses
    /// <see cref="DefaultFrameReadIdleTimeout"/>.
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
    /// Whether this connection is believed to be live. This is a best-effort <em>hint</em>: it
    /// combines the disposed flag with <see cref="System.Net.Sockets.TcpClient.Connected"/>, which
    /// reflects only the last known socket state and does not probe the wire. A dropped connection
    /// is not observed here until the next send/receive fails — rely on I/O exceptions for the
    /// authoritative state.
    /// </summary>
    public bool IsConnected => _client.Connected && Volatile.Read(ref _disposed) == 0;

    public string RemoteEndpoint { get; }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        SendValueAsync(data, ct).AsTask();

    public async ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        ct.ThrowIfCancellationRequested();

        // Reject malformed/oversized frames locally rather than shipping them to the peer, matching
        // StreamConnection and the inbound length check in ReceiveAsync below.
        MessageFramer.ValidateOutgoingFrame(data.Span);

        await WaitForSendSlotAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // DisposeAsync disposed the send lock while this send was in flight; the real
                // I/O fault (if any) already propagates from the WriteAsync above.
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
        ThrowIfDisposed();
        ReceiveConcurrencyGuard.Enter(ref _activeReceive, nameof(TcpConnection));
        Payload? payload = null;
        try
        {
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
                            StreamFrameReadProgressFormat.Body);
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
                MessageFrameReader.ValidateIncomingFrameLength(totalLength, MessageFramer.MaxMessageSize);

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
                    ReceiveConcurrencyGuard.Exit(ref _activeReceive);
                }
            }
        }
    }

    public async ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default)
    {
        try
        {
            await SendValueAsync(frame.WrittenMemory, ct).ConfigureAwait(false);
        }
        finally
        {
            frame.Dispose();
        }
    }

    public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default)
    {
        var pending = ReceiveFrameValueAsync(ct);
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return default;
        }

        _disposeCts.Cancel();
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
            throw new ObjectDisposedException(nameof(TcpConnection));
        }
    }
}
