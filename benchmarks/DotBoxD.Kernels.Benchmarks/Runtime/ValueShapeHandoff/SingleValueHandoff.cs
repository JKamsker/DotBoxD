using System.Runtime.ExceptionServices;

namespace DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff;

/// <summary>Coordinates a producer and consumer without including synchronization in measured work.</summary>
internal sealed class SingleValueHandoff<T> : IDisposable
    where T : class
{
    private readonly AutoResetEvent _available = new(initialState: false);
    private readonly AutoResetEvent _released = new(initialState: true);
    private T? _value;
    private Exception? _error;
    private int _aborted;

    public void WaitToProduce()
    {
        _released.WaitOne();
        ThrowIfAborted();
    }

    public void Publish(T value)
    {
        _value = value;
        _available.Set();
    }

    public T Take()
    {
        _available.WaitOne();
        ThrowIfAborted();
        return _value ?? throw new InvalidOperationException("The handoff published no value.");
    }

    public void Release()
    {
        _value = null;
        _released.Set();
    }

    public void Abort(Exception error)
    {
        _error = error;
        Volatile.Write(ref _aborted, 1);
        _available.Set();
        _released.Set();
    }

    public void Dispose()
    {
        _available.Dispose();
        _released.Dispose();
    }

    private void ThrowIfAborted()
    {
        if (Volatile.Read(ref _aborted) != 0)
        {
            ExceptionDispatchInfo.Capture(_error!).Throw();
        }
    }
}
