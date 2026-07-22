using System.Threading.Tasks.Sources;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

internal sealed class ControlledPendingFrameStream : Stream
{
    private readonly Func<object?>? _captureContext;
    private readonly List<object?> _observedContexts = new();
    private readonly Queue<PendingRead> _pendingReads = new();
    private readonly SemaphoreSlim _readStarted = new(0);
    private readonly byte[] _source;
    private int _offset;

    public ControlledPendingFrameStream(byte[] source, Func<object?>? captureContext = null)
    {
        _source = source;
        _captureContext = captureContext;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _source.Length;

    public override long Position
    {
        get => Volatile.Read(ref _offset);
        set => throw new NotSupportedException();
    }

    public object? GetObservedContext(int index)
    {
        lock (_pendingReads)
        {
            return _observedContexts[index];
        }
    }

    public async Task WaitForReadAsync(TimeSpan timeout)
    {
        if (!await _readStarted.WaitAsync(timeout))
        {
            throw new TimeoutException("The connection did not start its next stream read.");
        }
    }

    public void CompleteNextRead()
    {
        PendingRead pending;
        lock (_pendingReads)
        {
            pending = _pendingReads.Dequeue();
        }

        var offset = Volatile.Read(ref _offset);
        var count = Math.Min(pending.Destination.Length, _source.Length - offset);
        _source.AsMemory(offset, count).CopyTo(pending.Destination);
        Interlocked.Add(ref _offset, count);
        pending.Complete(count);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new PendingRead(buffer);
        lock (_pendingReads)
        {
            _observedContexts.Add(_captureContext?.Invoke());
            _pendingReads.Enqueue(pending);
        }

        _readStarted.Release();
        return pending.ValueTask;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    private sealed class PendingRead : IValueTaskSource<int>
    {
        private ManualResetValueTaskSourceCore<int> _source;

        public PendingRead(Memory<byte> destination) => Destination = destination;

        public Memory<byte> Destination { get; }

        public ValueTask<int> ValueTask => new(this, _source.Version);

        public void Complete(int count) => _source.SetResult(count);

        public int GetResult(short token) => _source.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);
    }
}
