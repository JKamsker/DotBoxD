namespace DotBoxD.Services.Transport;

/// <summary>Caps one transport-operation population at the number its pool can retain.</summary>
internal static class BoundedTransportOperationCreationBudget<TOperation>
    where TOperation : class
{
    private static int _createdCount;

    public static bool TryReserve(out bool reachedCapacity)
    {
        reachedCapacity = false;
        // Successful reservations are lifetime-scoped. If a completed source is abandoned before
        // consumption, later operations fail soft to a task-backed overflow path instead of
        // replacing it and allowing the reusable-source population to grow without a bound.
        var count = Volatile.Read(ref _createdCount);
        while (count < BoundedTransportOperationPool<TOperation>.MaxRetainedCount)
        {
            var observed = Interlocked.CompareExchange(ref _createdCount, count + 1, count);
            if (observed == count)
            {
                reachedCapacity = count + 1 ==
                    BoundedTransportOperationPool<TOperation>.MaxRetainedCount;
                return true;
            }

            count = observed;
        }

        return false;
    }

    public static void CancelReservation()
    {
        var count = Volatile.Read(ref _createdCount);
        while (true)
        {
            if (count == 0)
            {
                throw new InvalidOperationException(
                    "No transport-operation creation is reserved.");
            }

            var observed = Interlocked.CompareExchange(ref _createdCount, count - 1, count);
            if (observed == count)
            {
                return;
            }

            count = observed;
        }
    }
}
