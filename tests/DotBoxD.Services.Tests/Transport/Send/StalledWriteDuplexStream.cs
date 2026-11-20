using System.IO.Pipelines;

namespace DotBoxD.Services.Tests.Transport;

/// <summary>
/// One end of an in-memory duplex byte stream. When <c>stallWrites</c> is set, every write parks until
/// the token it was given is cancelled (or <see cref="ReleaseWrites"/> is called), modelling a borrowed
/// stream whose peer stopped reading: nothing but cancellation can interrupt the stalled write.
/// </summary>
internal sealed class StalledWriteDuplexStream : Stream
{
    private readonly Stream _reader;
    private readonly Stream _writer;
    private readonly bool _stallWrites;
    private readonly TaskCompletionSource _writeEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _writesReleased =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private StalledWriteDuplexStream(Pipe inbound, Pipe outbound, bool stallWrites)
    {
        _reader = inbound.Reader.AsStream();
        _writer = outbound.Writer.AsStream();
        _stallWrites = stallWrites;
    }

    public Task WriteEntered => _writeEntered.Task;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Creates a connected pair; only <c>stalled</c> parks its writes.</summary>
    public static (StalledWriteDuplexStream Stalled, StalledWriteDuplexStream Normal) CreatePair()
    {
        var toStalled = new Pipe();
        var toNormal = new Pipe();
        return (
            new StalledWriteDuplexStream(toStalled, toNormal, stallWrites: true),
            new StalledWriteDuplexStream(toNormal, toStalled, stallWrites: false));
    }

    public void ReleaseWrites() => _writesReleased.TrySetResult();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _reader.ReadAsync(buffer, cancellationToken);

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (_stallWrites)
        {
            _writeEntered.TrySetResult();
            await _writesReleased.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await _writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _writer.FlushAsync(cancellationToken);

    public override void Flush() => _writer.Flush();
    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writesReleased.TrySetResult();
            _reader.Dispose();
            _writer.Dispose();
        }

        base.Dispose(disposing);
    }
}
