using System.Diagnostics;
using System.IO.Pipes;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class NamedPipeHeldGateSendLane : IAsyncDisposable
{
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(10);
    private readonly NamedPipeServerStream _peer;
    private readonly NamedPipeClientStream _sender;
    private readonly StreamConnection _connection;
    private readonly PendingSendKind _kind;
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenSource _readTimeout = new(TimeSpan.FromMinutes(2));
    private readonly byte[] _receiveBuffer = new byte[SendProbeFrame.Length];
    private readonly SendProbeFrameLease[]? _ownedLeases;
    private int _ownedLeaseCount;
    private long _bytes;
    private long _checksum;
    private long _writes;

    private NamedPipeHeldGateSendLane(
        NamedPipeServerStream peer,
        NamedPipeClientStream sender,
        PendingSendKind kind,
        CancellationToken cancellationToken,
        int totalOperations)
    {
        _peer = peer;
        _sender = sender;
        _connection = new StreamConnection(sender, ownsStream: false);
        _kind = kind;
        _cancellationToken = cancellationToken;
        _ownedLeases = kind == PendingSendKind.Owned
            ? new SendProbeFrameLease[totalOperations]
            : null;
    }

    public static async Task<NamedPipeHeldGateSendLane> CreateAsync(
        PendingSendKind kind,
        CancellationToken cancellationToken,
        int totalOperations)
    {
        var pipeName = $"dotboxd-send-stage-{Guid.NewGuid():N}";
        var peer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var sender = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            var accepting = peer.WaitForConnectionAsync();
            await sender.ConnectAsync().WaitAsync(SetupTimeout).ConfigureAwait(false);
            await accepting.WaitAsync(SetupTimeout).ConfigureAwait(false);
            return new NamedPipeHeldGateSendLane(
                peer,
                sender,
                kind,
                cancellationToken,
                totalOperations);
        }
        catch
        {
            sender.Dispose();
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
            throw new InvalidOperationException("Not every named-pipe owned-frame lease was recorded.");
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
            throw new InvalidOperationException("The named-pipe send gate could not be held.");
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
                throw new InvalidOperationException(
                    "The named-pipe send did not remain behind the held gate.");
            }

            _ = frame?.WrittenMemory;
            _connection.SendGate.Release();
            gateHeldByHarness = false;
            PendingSendCompletion.Consume(ref pending);
            ReadExactly(_peer, _receiveBuffer, _readTimeout.Token);
            if (!_receiveBuffer.AsSpan().SequenceEqual(SendProbeFrame.Raw))
            {
                throw new InvalidOperationException("The named-pipe peer received unexpected frame bytes.");
            }

            _writes++;
            _bytes += _receiveBuffer.Length;
            _checksum += SendProbeFrame.CalculateChecksum(_receiveBuffer);
            if (_connection.SendGate.CurrentCount != 1)
            {
                throw new InvalidOperationException("The named-pipe send gate was not restored.");
            }

            return new PendingSendCallSample(elapsedTicks, callerAllocated);
        }
        finally
        {
            if (gateHeldByHarness)
            {
                _connection.SendGate.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _readTimeout.Dispose();
        _sender.Dispose();
        _peer.Dispose();
    }

    private static void ReadExactly(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        while (!buffer.IsEmpty)
        {
            var pending = stream.ReadAsync(buffer, cancellationToken);
            var read = PendingSendCompletion.Consume(ref pending);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "The named-pipe peer closed before one frame was drained.");
            }

            buffer = buffer.Slice(read);
        }
    }
}
