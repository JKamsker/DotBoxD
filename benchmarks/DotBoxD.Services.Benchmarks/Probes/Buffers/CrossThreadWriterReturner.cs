using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class CrossThreadWriterReturner : IDisposable
{
    private readonly Thread _thread;
    private PooledBufferWriter? _writer;
    private Exception? _failure;
    private int _published;
    private int _completed;
    private int _nextSequence;
    private int _stopping;

    public CrossThreadWriterReturner()
    {
        _thread = new Thread(ReturnLoop)
        {
            IsBackground = true,
            Name = "PooledBufferWriter benchmark returner",
        };
        _thread.Start();
    }

    public void Return(PooledBufferWriter writer)
    {
        var sequence = ++_nextSequence;
        _writer = writer;
        Volatile.Write(ref _published, sequence);

        while (Volatile.Read(ref _completed) != sequence)
        {
            Thread.SpinWait(1);
        }

        ThrowIfFailed();
    }

    public void Dispose()
    {
        Volatile.Write(ref _stopping, 1);
        _thread.Join();
        ThrowIfFailed();
    }

    private void ReturnLoop()
    {
        var sequence = 1;
        while (true)
        {
            while (Volatile.Read(ref _published) < sequence)
            {
                if (Volatile.Read(ref _stopping) != 0)
                {
                    return;
                }

                Thread.SpinWait(1);
            }

            var writer = _writer;
            _writer = null;
            try
            {
                (writer ?? throw new InvalidOperationException("No writer was published.")).Dispose();
            }
            catch (Exception ex)
            {
                _failure = ex;
            }
            finally
            {
                Volatile.Write(ref _completed, sequence);
            }

            sequence++;
        }
    }

    private void ThrowIfFailed()
    {
        if (_failure is { } failure)
        {
            throw new InvalidOperationException("Cross-thread writer return failed.", failure);
        }
    }
}
