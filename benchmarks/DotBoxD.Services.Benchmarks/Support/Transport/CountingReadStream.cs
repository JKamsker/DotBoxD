using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Support.Transport;

internal sealed class CountingReadStream : Stream, IStreamReceiveLookaheadCapable
{
    private readonly Stream _inner;
    private long _pendingReadCount;
    private long _readCount;

    public CountingReadStream(Stream inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public long PendingReadCount => Interlocked.Read(ref _pendingReadCount);

    public long ReadCount => Interlocked.Read(ref _readCount);

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        Interlocked.Increment(ref _readCount);
        return _inner.Read(buffer, offset, count);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _readCount);
        var pending = _inner.ReadAsync(buffer, offset, count, cancellationToken);
        if (!pending.IsCompleted)
        {
            Interlocked.Increment(ref _pendingReadCount);
        }

        return pending;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _readCount);
        var pending = _inner.ReadAsync(buffer, cancellationToken);
        if (!pending.IsCompleted)
        {
            Interlocked.Increment(ref _pendingReadCount);
        }

        return pending;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) =>
        _inner.Write(buffer, offset, count);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        _inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => _inner.DisposeAsync();
}
