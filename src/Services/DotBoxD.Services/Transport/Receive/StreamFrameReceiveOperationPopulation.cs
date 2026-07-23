using System.Runtime.CompilerServices;
using CreationBudget = DotBoxD.Services.Transport.BoundedFrameReceiveOperationCreationBudget<DotBoxD.Services.Transport.StreamFrameReceiveOperation>;

namespace DotBoxD.Services.Transport;

/// <summary>Bounds reusable Stream receive sources and selects overflow behavior.</summary>
internal static class StreamFrameReceiveOperationPopulation
{
    private static bool _isAtCapacity;
    private static bool _requiresPreflight;

    // A stale false only selects the safe transferred-state fallback for that receive.
    public static bool RequiresPreflight => _requiresPreflight;
    public static bool IsAtCapacity => _isAtCapacity;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool MustUseFallback()
    {
        if (!StreamFrameReceiveOperation.HasAvailableOperationForPopulation)
        {
            return true;
        }

        // Returned sources make preflight unnecessary until a pending receive drains the pool.
        _requiresPreflight = false;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ObserveAcquiredOperation()
    {
        if (!StreamFrameReceiveOperation.HasAvailableOperationForPopulation)
        {
            _requiresPreflight = true;
        }
    }

    public static StreamFrameReceiveOperation? CreateOrRentRaced()
    {
        if (!CreationBudget.TryReserve(out var reachedCapacity))
        {
            // A source may have returned after the initiating hot-slot miss.
            var operation = StreamFrameReceiveOperation.TryRentOperationForPopulation();
            if (operation is null)
            {
                // Heal a stale false hint after this receive takes the race fallback.
                _requiresPreflight = true;
            }

            return operation;
        }

        try
        {
            var operation = new StreamFrameReceiveOperation();
            if (reachedCapacity)
            {
                _isAtCapacity = true;
            }

            return operation;
        }
        catch
        {
            CreationBudget.CancelReservation();
            throw;
        }
    }
}
