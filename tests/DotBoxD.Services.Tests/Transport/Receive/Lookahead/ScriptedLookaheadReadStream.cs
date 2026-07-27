using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Tests.Transport.Receive.Lookahead;

internal sealed class ScriptedLookaheadReadStream : Stream, IStreamReceiveLookaheadCapable
{
    private readonly byte[] _source;
    private readonly int[] _readLimits;
    private readonly int _gatedReadIndex;
    private readonly List<int> _requestedReadLengths = new();
    private readonly TaskCompletionSource _gatedReadStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseGatedRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _offset;
    private int _readCalls;
    private bool _disposed;

    public ScriptedLookaheadReadStream(byte[] source, params int[] readLimits)
        : this(source, readLimits, gatedReadIndex: -1)
    {
    }

    public ScriptedLookaheadReadStream(
        byte[] source,
        int[] readLimits,
        int gatedReadIndex)
    {
        _source = source;
        _readLimits = readLimits;
        _gatedReadIndex = gatedReadIndex;
    }

    public int BytesConsumed => _offset;

    public bool IsDisposed => _disposed;

    public int ReadCalls => _readCalls;

    public IReadOnlyList<int> RequestedReadLengths => _requestedReadLengths;

    public Task WaitForGatedReadAsync(TimeSpan timeout) =>
        _gatedReadStarted.Task.WaitAsync(timeout);

    public void ReleaseGatedRead() => _releaseGatedRead.TrySetResult();

    public override bool CanRead => !_disposed;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _source.Length;

    public override long Position
    {
        get => _offset;
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var readCall = _readCalls++;
        _requestedReadLengths.Add(buffer.Length);
        if (readCall == _gatedReadIndex)
        {
            _gatedReadStarted.TrySetResult();
            return AwaitGateAndReadAsync(buffer, cancellationToken, readCall);
        }

        return ValueTask.FromResult(CopyTo(buffer, readCall));
    }

    private async ValueTask<int> AwaitGateAndReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken,
        int readCall)
    {
        await _releaseGatedRead.Task.WaitAsync(cancellationToken);
        return CopyTo(buffer, readCall);
    }

    private int CopyTo(Memory<byte> buffer, int readCall)
    {
        if (_offset == _source.Length)
        {
            return 0;
        }

        var limit = readCall < _readLimits.Length ? _readLimits[readCall] : int.MaxValue;
        var count = Math.Min(Math.Min(buffer.Length, limit), _source.Length - _offset);
        _source.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class RecordingExactReadStream(byte[] source) : MemoryStream(source, writable: false)
{
    private readonly List<int> _requestedReadLengths = new();

    public IReadOnlyList<int> RequestedReadLengths => _requestedReadLengths;

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        _requestedReadLengths.Add(buffer.Length);
        return base.ReadAsync(buffer, cancellationToken);
    }
}
