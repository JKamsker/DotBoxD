using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class TcpHeldGateSendLane : IAsyncDisposable
{
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(10);
    private readonly TcpConnection _connection;
    private readonly TcpClient _peer;
    private readonly NetworkStream _peerStream;
    private readonly PendingSendKind _kind;
    private readonly CancellationToken _cancellationToken;
    private readonly byte[] _receiveBuffer = new byte[SendProbeFrame.Length];
    private readonly SendProbeFrameLease[]? _ownedLeases;
    private int _ownedLeaseCount;
    private long _bytes;
    private long _checksum;
    private long _writes;

    private TcpHeldGateSendLane(
        TcpConnection connection,
        TcpClient peer,
        PendingSendKind kind,
        CancellationToken cancellationToken,
        int totalOperations)
    {
        _connection = connection;
        _peer = peer;
        _peerStream = peer.GetStream();
        _kind = kind;
        _cancellationToken = cancellationToken;
        _ownedLeases = kind == PendingSendKind.Owned
            ? new SendProbeFrameLease[totalOperations]
            : null;
    }

    public static async Task<TcpHeldGateSendLane> CreateAsync(
        PendingSendKind kind,
        CancellationToken cancellationToken,
        int totalOperations)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var peer = new TcpClient(AddressFamily.InterNetwork)
        {
            NoDelay = true,
            ReceiveTimeout = 10_000,
        };
        TcpClient? sender = null;
        try
        {
            var accepting = listener.AcceptTcpClientAsync();
            await peer.ConnectAsync(endpoint.Address, endpoint.Port)
                .WaitAsync(SetupTimeout)
                .ConfigureAwait(false);
            sender = await accepting.WaitAsync(SetupTimeout).ConfigureAwait(false);
            sender.NoDelay = true;
            var lane = new TcpHeldGateSendLane(
                new TcpConnection(sender, Timeout.InfiniteTimeSpan),
                peer,
                kind,
                cancellationToken,
                totalOperations);
            sender = null;
            return lane;
        }
        catch
        {
            sender?.Dispose();
            peer.Dispose();
            throw;
        }
    }

    public SendOutputSnapshot Snapshot() => new(_writes, Flushes: 0, _bytes, _checksum);

    public void VerifyOwnedFramesDisposed()
    {
        if (_ownedLeases is null)
        {
            return;
        }

        if (_ownedLeaseCount != _ownedLeases.Length)
        {
            throw new InvalidOperationException("Not every TCP owned-frame lease was recorded.");
        }

        foreach (var lease in _ownedLeases)
        {
            SendProbeFrame.AssertDisposed(lease);
        }
    }

    public PendingSendCallSample SendOnce()
    {
        if (_connection.SendGate.CurrentCount != 1 || !_connection.SendGate.Wait(0))
        {
            throw new InvalidOperationException("The TCP send gate could not be held.");
        }

        var gateHeldByHarness = true;
        PooledBufferWriter? frame = _kind == PendingSendKind.Owned
            ? SendProbeFrame.Rent()
            : null;
        if (frame is not null)
        {
            _ownedLeases![_ownedLeaseCount++] = SendProbeFrame.CaptureLease(frame);
        }

        try
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var startedAt = Stopwatch.GetTimestamp();
            var pending = frame is null
                ? _connection.SendValueAsync(SendProbeFrame.Raw, _cancellationToken)
                : _connection.SendFrameValueAsync(frame, _cancellationToken);
            var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            var callerAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            if (pending.IsCompleted || _connection.SendGate.CurrentCount != 0)
            {
                throw new InvalidOperationException("The TCP send did not remain behind the held gate.");
            }

            _ = frame?.WrittenMemory;
            _connection.ReleaseSendGate();
            gateHeldByHarness = false;
            PendingSendCompletion.Consume(ref pending);

            ReadExactly(_peerStream, _receiveBuffer);
            if (!_receiveBuffer.AsSpan().SequenceEqual(SendProbeFrame.Raw))
            {
                throw new InvalidOperationException("The TCP peer received unexpected frame bytes.");
            }

            _writes++;
            _bytes += _receiveBuffer.Length;
            _checksum += SendProbeFrame.CalculateChecksum(_receiveBuffer);
            if (_connection.SendGate.CurrentCount != 1)
            {
                throw new InvalidOperationException("The TCP send gate was not restored.");
            }

            return new PendingSendCallSample(elapsedTicks, callerAllocated);
        }
        finally
        {
            if (gateHeldByHarness)
            {
                _connection.ReleaseSendGate();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _peer.Dispose();
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read == 0)
            {
                throw new EndOfStreamException("The TCP peer closed before one frame was drained.");
            }

            buffer = buffer.Slice(read);
        }
    }
}
