namespace DotBoxD.Services.Transport;

/// <summary>Retains two reusable operations for one connection after shared-pool overflow.</summary>
internal abstract class DedicatedFrameReceiveOperationCache<TOperation>
    where TOperation : class
{
    private const int MaxSourceCount = 2;

    private TOperation? _first;
    private TOperation? _second;
    private int _createdCount;
    private int _disposed;

    public bool CanAcquire =>
        Volatile.Read(ref _disposed) == 0 &&
        (Volatile.Read(ref _first) is not null ||
         Volatile.Read(ref _second) is not null ||
         Volatile.Read(ref _createdCount) < MaxSourceCount);

    public int CreatedCount => Volatile.Read(ref _createdCount);

    public int AvailableCount =>
        (Volatile.Read(ref _first) is null ? 0 : 1) +
        (Volatile.Read(ref _second) is null ? 0 : 1);

    public TOperation? TryRent()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        return Interlocked.Exchange(ref _first, null) ??
               Interlocked.Exchange(ref _second, null);
    }

    public TOperation? TryCreate()
    {
        if (!TryReserveSource())
        {
            return null;
        }

        try
        {
            var operation = CreateOperation();
            return Volatile.Read(ref _disposed) == 0 ? operation : null;
        }
        catch
        {
            Interlocked.Decrement(ref _createdCount);
            throw;
        }
    }

    public void Return(TOperation operation)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _first, operation, null) is null ||
            Interlocked.CompareExchange(ref _second, operation, null) is null)
        {
            RemoveReturnedOperationAfterDispose(operation);
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _ = Interlocked.Exchange(ref _first, null);
        _ = Interlocked.Exchange(ref _second, null);
    }

    protected abstract TOperation CreateOperation();

    private bool TryReserveSource()
    {
        while (Volatile.Read(ref _disposed) == 0)
        {
            var createdCount = Volatile.Read(ref _createdCount);
            if (createdCount == MaxSourceCount)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref _createdCount,
                    createdCount + 1,
                    createdCount) == createdCount)
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    return true;
                }

                Interlocked.Decrement(ref _createdCount);
                return false;
            }
        }

        return false;
    }

    private void RemoveReturnedOperationAfterDispose(TOperation operation)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            return;
        }

        _ = Interlocked.CompareExchange(ref _first, null, operation);
        _ = Interlocked.CompareExchange(ref _second, null, operation);
    }
}
