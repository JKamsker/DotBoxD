using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace DotBoxD.Services.Benchmarks.Support.Transport;

internal sealed class GatedFrameWriter : IDisposable
{
    private readonly AutoResetEvent _completion = new(initialState: false);
    private readonly byte[] _frame;
    private readonly AutoResetEvent _request = new(initialState: false);
    private readonly Stream _stream;
    private readonly Thread _thread;
    private ExceptionDispatchInfo? _failure;
    private long _allocatedBytes;
    private int _disposed;
    private long _writeStartedAt;

    public GatedFrameWriter(Stream stream, byte[] frame)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _thread = new Thread(WriteFrames)
        {
            IsBackground = true,
            Name = nameof(GatedFrameWriter),
        };
        _thread.Start();
    }

    public long AllocatedBytes => Interlocked.Read(ref _allocatedBytes);

    public void ReleaseFrame()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _request.Set();
    }

    public long WaitForCompletion()
    {
        _completion.WaitOne();
        _failure?.Throw();
        return Volatile.Read(ref _writeStartedAt);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _request.Set();
        _thread.Join();
        _request.Dispose();
        _completion.Dispose();
    }

    private void WriteFrames()
    {
        while (true)
        {
            _request.WaitOne();
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                Volatile.Write(ref _writeStartedAt, Stopwatch.GetTimestamp());
                _stream.Write(_frame);
                _stream.Flush();
            }
            catch (Exception exception)
            {
                _failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                Interlocked.Add(
                    ref _allocatedBytes,
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
                _completion.Set();
            }

            if (_failure is not null)
            {
                return;
            }
        }
    }
}
