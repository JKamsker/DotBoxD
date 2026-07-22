using System.Threading.Tasks.Sources;

namespace DotBoxD.Services.Benchmarks.Support;

internal sealed class ForcedPendingFrameStream : Stream
{
    private readonly byte[] _frame;
    private readonly AutoResetEvent _completionRequested = new(initialState: false);
    private readonly Thread _completionThread;
    private readonly PendingReadSource[] _readSources;
    private PendingReadSource? _queuedSource;
    private int _frameOffset;
    private int _nextSource;
    private int _disposed;
    private int _stopping;
    private long _readCount;
    private long _pendingReadCount;
    private long _completedReadCount;
    private long _bytesRead;

    public ForcedPendingFrameStream(byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Length == 0)
        {
            throw new ArgumentException("A repeated frame cannot be empty.", nameof(frame));
        }

        _frame = frame;
        // Inline completion mirrors an I/O completion thread without adding ThreadPool work-item
        // allocations. Alternating sources keeps Reset away from the SetResult call that is still
        // unwinding the preceding read's continuation.
        _readSources = [new PendingReadSource(this), new PendingReadSource(this)];
        _completionThread = new Thread(CompleteReads)
        {
            IsBackground = true,
            Name = nameof(ForcedPendingFrameStream),
        };
        _completionThread.Start();
    }

    public long ReadCount => Interlocked.Read(ref _readCount);

    public long PendingReadCount => Interlocked.Read(ref _pendingReadCount);

    public long CompletedReadCount => Interlocked.Read(ref _completedReadCount);

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    public override bool CanRead => Volatile.Read(ref _disposed) == 0;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty)
        {
            return new ValueTask<int>(0);
        }

        var source = _readSources[_nextSource];
        _nextSource ^= 1;
        source.Prepare(buffer);
        Interlocked.Increment(ref _readCount);
        return source.CreateValueTask();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Only asynchronous reads are supported by this probe stream.");

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!disposing || Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            base.Dispose(disposing);
            return;
        }

        Volatile.Write(ref _stopping, 1);
        _completionRequested.Set();
        _completionThread.Join();
        _completionRequested.Dispose();
        base.Dispose(disposing);
    }

    private void QueueCompletion(PendingReadSource source)
    {
        if (Interlocked.CompareExchange(ref _queuedSource, source, comparand: null) is not null)
        {
            throw new InvalidOperationException("The prior pending read has not completed.");
        }

        Interlocked.Increment(ref _pendingReadCount);
        _completionRequested.Set();
    }

    private void CompleteReads()
    {
        while (true)
        {
            _completionRequested.WaitOne();
            if (Volatile.Read(ref _stopping) != 0)
            {
                return;
            }

            var source = Interlocked.Exchange(ref _queuedSource, null) ??
                throw new InvalidOperationException("A pending-read signal had no source.");
            var destination = source.Destination;
            if (destination.Length > _frame.Length - _frameOffset)
            {
                throw new InvalidOperationException("A probe read crossed a repeated-frame boundary.");
            }

            _frame.AsMemory(_frameOffset, destination.Length).CopyTo(destination);
            _frameOffset += destination.Length;
            if (_frameOffset == _frame.Length)
            {
                _frameOffset = 0;
            }

            Interlocked.Add(ref _bytesRead, destination.Length);
            Interlocked.Increment(ref _completedReadCount);
            source.Complete(destination.Length);
        }
    }

    private sealed class PendingReadSource : IValueTaskSource<int>
    {
        private readonly ForcedPendingFrameStream _owner;
        private ManualResetValueTaskSourceCore<int> _source;
        private Memory<byte> _destination;
        private int _pending;

        public PendingReadSource(ForcedPendingFrameStream owner) => _owner = owner;

        public Memory<byte> Destination => _destination;

        public void Prepare(Memory<byte> destination)
        {
            if (Interlocked.Exchange(ref _pending, 1) != 0)
            {
                throw new InvalidOperationException("A reusable read source is still pending.");
            }

            _source.Reset();
            _destination = destination;
        }

        public ValueTask<int> CreateValueTask() => new(this, _source.Version);

        public void Complete(int count) => _source.SetResult(count);

        public int GetResult(short token)
        {
            try
            {
                return _source.GetResult(token);
            }
            finally
            {
                _destination = default;
                Volatile.Write(ref _pending, 0);
            }
        }

        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            _source.OnCompleted(continuation, state, token, flags);
            _owner.QueueCompletion(this);
        }
    }
}
