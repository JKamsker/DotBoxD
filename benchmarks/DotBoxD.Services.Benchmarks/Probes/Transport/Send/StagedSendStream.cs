using System.Threading.Tasks.Sources;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class StagedSendStream : Stream, IValueTaskSource
{
    private readonly PendingSendStage _stage;
    private readonly CancellationToken _expectedToken;
    private readonly TaskCompletionSource[]? _flushCompletions;
    private ManualResetValueTaskSourceCore<bool> _pendingWrite;
    private ReadOnlyMemory<byte> _pendingBuffer;
    private TaskCompletionSource? _activeFlush;
    private long _bytes;
    private long _checksum;
    private long _flushes;
    private long _writes;
    private int _nextFlush;
    private int _pendingConsumed;
    private int _pendingState;

    public StagedSendStream(
        PendingSendStage stage,
        CancellationToken expectedToken,
        int totalOperations)
    {
        _stage = stage;
        _expectedToken = expectedToken;
        _pendingWrite.RunContinuationsAsynchronously = false;
        if (stage == PendingSendStage.Flush)
        {
            _flushCompletions = new TaskCompletionSource[totalOperations];
            for (var index = 0; index < _flushCompletions.Length; index++)
            {
                // Inline completion keeps transport continuation work on the measured caller.
                _flushCompletions[index] = new TaskCompletionSource(TaskCreationOptions.None);
            }
        }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public SendOutputSnapshot Snapshot() => new(_writes, _flushes, _bytes, _checksum);

    public void CompletePendingOperation()
    {
        if (_stage == PendingSendStage.Write)
        {
            if (Interlocked.CompareExchange(ref _pendingState, 2, 1) != 1)
            {
                throw new InvalidOperationException("No controlled write is pending.");
            }

            RecordWrite(_pendingBuffer.Span);
            _pendingWrite.SetResult(true);
            return;
        }

        if (_stage == PendingSendStage.Flush)
        {
            var completion = _activeFlush ??
                throw new InvalidOperationException("No controlled flush is pending.");
            _activeFlush = null;
            completion.SetResult();
            return;
        }

        throw new InvalidOperationException("The send gate is completed by the lane, not the stream.");
    }

    public void ResetCompletedOperation()
    {
        if (_stage != PendingSendStage.Write)
        {
            return;
        }

        if (Volatile.Read(ref _pendingState) != 2 ||
            Volatile.Read(ref _pendingConsumed) == 0)
        {
            throw new InvalidOperationException("The controlled write was not consumed.");
        }

        _pendingWrite.Reset();
        _pendingBuffer = default;
        Volatile.Write(ref _pendingConsumed, 0);
        Volatile.Write(ref _pendingState, 0);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        VerifyToken(cancellationToken);
        if (_stage != PendingSendStage.Write)
        {
            RecordWrite(buffer.Span);
            return ValueTask.CompletedTask;
        }

        _pendingBuffer = buffer;
        if (Interlocked.CompareExchange(ref _pendingState, 1, 0) != 0)
        {
            _pendingBuffer = default;
            throw new InvalidOperationException("Only one write may be pending.");
        }

        return new ValueTask(this, _pendingWrite.Version);
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        VerifyToken(cancellationToken);
        _flushes++;
        if (_stage != PendingSendStage.Flush)
        {
            return Task.CompletedTask;
        }

        if (_activeFlush is not null || _flushCompletions is null)
        {
            throw new InvalidOperationException("Only one flush may be pending.");
        }

        if (_nextFlush == _flushCompletions.Length)
        {
            throw new InvalidOperationException("The preallocated flush completion set was exhausted.");
        }

        _activeFlush = _flushCompletions[_nextFlush++];
        return _activeFlush.Task;
    }

    void IValueTaskSource.GetResult(short token)
    {
        _pendingWrite.GetResult(token);
        Volatile.Write(ref _pendingConsumed, 1);
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) =>
        _pendingWrite.GetStatus(token);

    void IValueTaskSource.OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags) =>
        _pendingWrite.OnCompleted(continuation, state, token, flags);

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void RecordWrite(ReadOnlySpan<byte> buffer)
    {
        _writes++;
        _bytes += buffer.Length;
        _checksum += SendProbeFrame.CalculateChecksum(buffer);
    }

    private void VerifyToken(CancellationToken cancellationToken)
    {
        if (cancellationToken != _expectedToken)
        {
            throw new InvalidOperationException("The transport did not forward the expected caller token.");
        }
    }
}
