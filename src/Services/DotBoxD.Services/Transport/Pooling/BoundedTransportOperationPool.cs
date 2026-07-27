namespace DotBoxD.Services.Transport;

/// <summary>Keeps a bounded process-wide cache for one reusable transport-operation type.</summary>
internal sealed class BoundedTransportOperationPool<TOperation>
    where TOperation : class
{
    internal const int MaxOverflowCount = 16;
    internal const int MaxRetainedCount = MaxOverflowCount + 1;

    private readonly object _gate = new();
    private readonly TOperation?[] _overflow = new TOperation[MaxOverflowCount];
    private TOperation? _cached;
    private int _overflowCount;

    public TOperation? Rent()
    {
        var operation = Interlocked.Exchange(ref _cached, null);
        if (operation is not null)
        {
            return operation;
        }

        lock (_gate)
        {
            if (_overflowCount == 0)
            {
                return null;
            }

            var index = --_overflowCount;
            operation = _overflow[index];
            _overflow[index] = null;
            return operation;
        }
    }

    public void Return(TOperation operation)
    {
        if (Interlocked.CompareExchange(ref _cached, operation, null) is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_overflowCount == _overflow.Length)
            {
                return;
            }

            _overflow[_overflowCount++] = operation;
        }
    }

    internal int RetainedCount
    {
        get
        {
            lock (_gate)
            {
                return _overflowCount + (Volatile.Read(ref _cached) is null ? 0 : 1);
            }
        }
    }

    // This is a nonmutating saturation hint, not a rent guarantee. A concurrent rent/return may
    // make either answer stale; callers still use Rent and choose an equivalent safe fallback.
    internal bool HasAvailable =>
        Volatile.Read(ref _cached) is not null || Volatile.Read(ref _overflowCount) != 0;
}
