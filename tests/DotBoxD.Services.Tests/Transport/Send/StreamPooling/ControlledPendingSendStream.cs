using System.Threading.Tasks.Sources;

namespace DotBoxD.Services.Tests.Transport.Send.StreamPooling;

[Flags]
internal enum PendingStreamSendStage
{
    None = 0,
    Write = 1,
    Flush = 2,
}

internal sealed class ControlledPendingSendStream : Stream
{
    private readonly PendingStreamSendStage _pendingStages;
    private readonly PendingWriteSource _pendingWrite = new();
    private readonly TaskCompletionSource _flushReleased = new();
    private readonly TaskCompletionSource _writeEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _flushEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ReadOnlyMemory<byte> _writeBuffer;
    private Exception? _writeFailure;
    private Exception? _flushFailure;
    private int _writeBlocked;
    private int _flushBlocked;

    public ControlledPendingSendStream(PendingStreamSendStage pendingStages) =>
        _pendingStages = pendingStages;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public CancellationToken FlushToken { get; private set; }
    public int FlushCount { get; private set; }
    public Func<object?>? ObserveContext { get; init; }
    public object? FlushContext { get; private set; }
    public CancellationToken WriteToken { get; private set; }
    public object? WriteContext { get; private set; }
    public int WriteCount { get; private set; }
    public int WriteResultCount => _pendingWrite.GetResultCount;
    public byte[]? WrittenBytes { get; private set; }
    public Task FlushEntered => _flushEntered.Task;
    public Task WriteEntered => _writeEntered.Task;

    public void CancelOrFailWrite(Exception error) => _pendingWrite.Fail(error);

    public void CompleteFlush() => _flushReleased.TrySetResult();

    public void CompleteWrite()
    {
        WrittenBytes = _writeBuffer.ToArray();
        _pendingWrite.Succeed();
    }

    public void FailFlush(Exception error)
    {
        _flushFailure = error;
        _flushReleased.TrySetResult();
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        WriteCount++;
        WriteToken = cancellationToken;
        WriteContext = ObserveContext?.Invoke();
        _writeEntered.TrySetResult();
        if ((_pendingStages & PendingStreamSendStage.Write) != 0 &&
            Interlocked.Exchange(ref _writeBlocked, 1) == 0)
        {
            _writeBuffer = buffer;
            return _pendingWrite.Start(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _writeFailure, null) is { } error)
        {
            throw error;
        }

        WrittenBytes = buffer.ToArray();
        return default;
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        FlushCount++;
        FlushToken = cancellationToken;
        FlushContext = ObserveContext?.Invoke();
        _flushEntered.TrySetResult();
        if ((_pendingStages & PendingStreamSendStage.Flush) != 0 &&
            Interlocked.Exchange(ref _flushBlocked, 1) == 0)
        {
            return WaitForFlushAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _flushFailure, null) is { } error)
        {
            return Task.FromException(error);
        }

        return Task.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    private async Task WaitForFlushAsync(CancellationToken cancellationToken)
    {
        await _flushReleased.Task.WaitAsync(cancellationToken);
        if (Interlocked.Exchange(ref _flushFailure, null) is { } error)
        {
            throw error;
        }
    }

    private sealed class PendingWriteSource : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _source;
        private CancellationToken _token;
        private CancellationTokenRegistration _registration;
        private int _completed;
        private int _getResultCount;

        public int GetResultCount => Volatile.Read(ref _getResultCount);

        public ValueTask Start(CancellationToken token)
        {
            _token = token;
            if (token.CanBeCanceled)
            {
                _registration = token.UnsafeRegister(
                    static state => ((PendingWriteSource)state!).Cancel(),
                    this);
            }

            return new ValueTask(this, _source.Version);
        }

        public void Succeed() => Complete(error: null, disposeRegistration: true);

        public void Fail(Exception error) => Complete(error, disposeRegistration: true);

        public void GetResult(short token)
        {
            Interlocked.Increment(ref _getResultCount);
            _source.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);

        private void Cancel() => Complete(
            new OperationCanceledException(_token),
            disposeRegistration: false);

        private void Complete(Exception? error, bool disposeRegistration)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            try
            {
                if (error is null)
                {
                    _source.SetResult(true);
                }
                else
                {
                    _source.SetException(error);
                }
            }
            finally
            {
                if (disposeRegistration)
                {
                    _registration.Dispose();
                }
            }
        }
    }
}
