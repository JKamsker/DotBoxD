using System.Diagnostics;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class StreamPendingSendLane : IDisposable
{
    private readonly StagedSendStream _stream;
    private readonly StreamConnection _connection;
    private readonly PendingSendStage _stage;
    private readonly PendingSendKind _kind;
    private readonly CancellationToken _cancellationToken;
    private PooledBufferWriter? _lastOwnedFrame;

    public StreamPendingSendLane(
        PendingSendStage stage,
        PendingSendKind kind,
        CancellationToken cancellationToken,
        int totalOperations)
    {
        _stage = stage;
        _kind = kind;
        _cancellationToken = cancellationToken;
        _stream = new StagedSendStream(stage, cancellationToken, totalOperations);
        _connection = new StreamConnection(_stream, ownsStream: false);
    }

    public SendOutputSnapshot Snapshot() => _stream.Snapshot();

    public void VerifyLastOwnedFrameDisposed()
    {
        if (_lastOwnedFrame is not null)
        {
            SendProbeFrame.AssertDisposed(_lastOwnedFrame);
        }
    }

    public PendingSendCallSample SendOnce()
    {
        if (_connection.SendGate.CurrentCount != 1)
        {
            throw new InvalidOperationException("The Stream send gate was not idle before the send.");
        }

        var gateHeldByHarness = _stage == PendingSendStage.Gate;
        if (gateHeldByHarness && !_connection.SendGate.Wait(0))
        {
            throw new InvalidOperationException("The Stream send gate could not be held.");
        }

        PooledBufferWriter? frame = _kind == PendingSendKind.Owned
            ? SendProbeFrame.Rent()
            : null;
        try
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var startedAt = Stopwatch.GetTimestamp();
            var pending = frame is null
                ? _connection.SendValueAsync(SendProbeFrame.Raw, _cancellationToken)
                : _connection.SendFrameValueAsync(frame, _cancellationToken);
            var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            var callerAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            if (pending.IsCompleted)
            {
                throw new InvalidOperationException($"The controlled {_stage} send did not remain pending.");
            }

            if (_connection.SendGate.CurrentCount != 0)
            {
                throw new InvalidOperationException("The pending send did not retain or wait for the gate.");
            }

            _ = frame?.WrittenMemory;
            if (gateHeldByHarness)
            {
                _connection.SendGate.Release();
                gateHeldByHarness = false;
            }
            else
            {
                _stream.CompletePendingOperation();
            }

            PendingSendCompletion.Consume(ref pending);
            _stream.ResetCompletedOperation();
            _lastOwnedFrame = frame;

            if (_connection.SendGate.CurrentCount != 1)
            {
                throw new InvalidOperationException("The Stream send gate was not restored.");
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

    public void Dispose()
    {
        _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _stream.Dispose();
    }
}
