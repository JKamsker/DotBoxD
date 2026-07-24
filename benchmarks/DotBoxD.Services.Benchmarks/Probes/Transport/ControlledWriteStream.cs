using System.Threading.Tasks.Sources;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class ControlledWriteStream : Stream, IValueTaskSource
{
    private ManualResetValueTaskSourceCore<bool> _pendingWrite;
    private ReadOnlyMemory<byte> _pendingBuffer;
    private long _bytes;
    private long _checksum;
    private long _flushes;
    private long _writes;
    private int _pendingConsumed;
    private int _pendingState;

    public ControlledWriteStream(bool forcePending)
    {
        ForcePending = forcePending;
        _pendingWrite.RunContinuationsAsynchronously = false;
    }

    public bool ForcePending { get; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public WriteSnapshot Snapshot() => new(_writes, _flushes, _bytes, _checksum);

    public void CompletePendingWrite()
    {
        if (Interlocked.CompareExchange(ref _pendingState, 2, 1) != 1)
        {
            throw new InvalidOperationException("No pending write is ready to complete.");
        }

        RecordWrite(_pendingBuffer.Span);
        _pendingWrite.SetResult(true);
    }

    public void ResetCompletedWrite()
    {
        if (Volatile.Read(ref _pendingState) != 2 ||
            Volatile.Read(ref _pendingConsumed) == 0)
        {
            throw new InvalidOperationException("The pending write has not completed and been consumed.");
        }

        // The send ValueTask is consumed before every reset, so no stale token survives reuse.
        _pendingWrite.Reset();
        _pendingBuffer = default;
        Volatile.Write(ref _pendingConsumed, 0);
        Volatile.Write(ref _pendingState, 0);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        RejectCancelableToken(cancellationToken);
        if (!ForcePending)
        {
            RecordWrite(buffer.Span);
            return ValueTask.CompletedTask;
        }

        _pendingBuffer = buffer;
        if (Interlocked.CompareExchange(ref _pendingState, 1, 0) != 0)
        {
            _pendingBuffer = default;
            throw new InvalidOperationException("Only one write may be pending at a time.");
        }

        return new ValueTask(this, _pendingWrite.Version);
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

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        RejectCancelableToken(cancellationToken);
        _flushes++;
        return Task.CompletedTask;
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void RecordWrite(ReadOnlySpan<byte> buffer)
    {
        _writes++;
        _bytes += buffer.Length;
        _checksum += CalculateChecksum(buffer);
    }

    private static void RejectCancelableToken(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            throw new InvalidOperationException(
                "This allocation probe accepts only CancellationToken.None.");
        }
    }

    private static long CalculateChecksum(ReadOnlySpan<byte> buffer)
    {
        var checksum = 0L;
        for (var index = 0; index < buffer.Length; index++)
        {
            checksum += (index + 1L) * buffer[index];
        }

        return checksum;
    }
}

internal readonly record struct WriteSnapshot(long Writes, long Flushes, long Bytes, long Checksum);
