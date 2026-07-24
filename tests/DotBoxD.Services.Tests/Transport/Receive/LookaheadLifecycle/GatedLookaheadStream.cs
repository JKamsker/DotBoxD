using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Tests.Transport.Receive.LookaheadLifecycle;

internal sealed class GatedLookaheadStream : Stream, IStreamReceiveLookaheadCapable
{
    private readonly byte[] _source;
    private readonly int _gatedReadCall;
    private readonly bool _gateDispose;
    private readonly TaskCompletionSource _readStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseDispose =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeCalls;
    private int _disposeStartedFlag;
    private int _offset;
    private int _readCalls;

    public GatedLookaheadStream(
        byte[] source,
        int gatedReadCall = 0,
        bool gateDispose = false)
    {
        _source = source;
        _gatedReadCall = gatedReadCall;
        _gateDispose = gateDispose;
    }

    public int DisposeCalls => Volatile.Read(ref _disposeCalls);

    public int ReadCalls => Volatile.Read(ref _readCalls);

    public override bool CanRead => Volatile.Read(ref _disposeStartedFlag) == 0;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _source.Length;

    public override long Position
    {
        get => Volatile.Read(ref _offset);
        set => throw new NotSupportedException();
    }

    public Task WaitForReadAsync(TimeSpan timeout) => _readStarted.Task.WaitAsync(timeout);

    public Task WaitForDisposeAsync(TimeSpan timeout) => _disposeStarted.Task.WaitAsync(timeout);

    public void ReleaseRead() => _releaseRead.TrySetResult();

    public void ReleaseDispose() => _releaseDispose.TrySetResult();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStartedFlag) != 0, this);
        var readCall = Interlocked.Increment(ref _readCalls);
        _readStarted.TrySetResult();

        if (readCall == _gatedReadCall)
        {
            await _releaseRead.Task.WaitAsync(cancellationToken);
        }

        var offset = Volatile.Read(ref _offset);
        var count = Math.Min(buffer.Length, _source.Length - offset);
        _source.AsMemory(offset, count).CopyTo(buffer);
        Interlocked.Add(ref _offset, count);
        return count;
    }

    public override async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCalls);
        Interlocked.Exchange(ref _disposeStartedFlag, 1);
        _disposeStarted.TrySetResult();

        if (_gateDispose)
        {
            await _releaseDispose.Task;
        }
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
}
