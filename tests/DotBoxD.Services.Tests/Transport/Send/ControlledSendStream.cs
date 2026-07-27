namespace DotBoxD.Services.Tests.Transport;

internal enum SendBlockPoint
{
    None,
    Write,
    Flush,
}

internal sealed class ControlledSendStream : Stream
{
    private readonly SendBlockPoint _blockPoint;
    private Exception? _writeFailure;
    private Exception? _flushFailure;
    private readonly TaskCompletionSource _writeEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _writeReleased =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _flushEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _flushReleased =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ControlledSendStream(
        SendBlockPoint blockPoint = SendBlockPoint.None,
        Exception? writeFailure = null,
        Exception? flushFailure = null)
    {
        _blockPoint = blockPoint;
        _writeFailure = writeFailure;
        _flushFailure = flushFailure;
    }

    public Task WriteEntered => _writeEntered.Task;
    public Task FlushEntered => _flushEntered.Task;
    public byte[]? WrittenBytes { get; private set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void ReleaseWrite() => _writeReleased.TrySetResult();
    public void ReleaseFlush() => _flushReleased.TrySetResult();

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        WriteCoreAsync(buffer, cancellationToken);

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        FlushCoreAsync(cancellationToken);

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private async ValueTask WriteCoreAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        _writeEntered.TrySetResult();
        if (_blockPoint == SendBlockPoint.Write)
        {
            await _writeReleased.Task.WaitAsync(cancellationToken);
        }

        if (Interlocked.Exchange(ref _writeFailure, null) is { } writeFailure)
        {
            throw writeFailure;
        }

        // Observe the caller's memory only when the simulated I/O completes.
        WrittenBytes = buffer.ToArray();
    }

    private async Task FlushCoreAsync(CancellationToken cancellationToken)
    {
        _flushEntered.TrySetResult();
        if (_blockPoint == SendBlockPoint.Flush)
        {
            await _flushReleased.Task.WaitAsync(cancellationToken);
        }

        if (Interlocked.Exchange(ref _flushFailure, null) is { } flushFailure)
        {
            throw flushFailure;
        }
    }
}
