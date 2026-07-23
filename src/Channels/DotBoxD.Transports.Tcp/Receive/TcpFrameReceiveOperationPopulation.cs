using System.Runtime.CompilerServices;
using CreationBudget = DotBoxD.Services.Transport.BoundedFrameReceiveOperationCreationBudget<DotBoxD.Transports.Tcp.TcpFrameReceiveOperation>;

namespace DotBoxD.Transports.Tcp;

/// <summary>Bounds reusable TCP receive sources and selects overflow behavior.</summary>
internal static class TcpFrameReceiveOperationPopulation
{
    private static bool _isAtCapacity;

    // A stale false only selects the safe transferred-state fallback for that receive.
    public static bool IsAtCapacity => _isAtCapacity;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool HasNoAvailableOperation() =>
        !TcpFrameReceiveOperation.HasAvailableOperationForPopulation;

    public static TcpFrameReceiveOperation? CreateOrRentRaced()
    {
        if (!CreationBudget.TryReserve(out var reachedCapacity))
        {
            // A source may have returned after the initiating hot-slot miss.
            return TcpFrameReceiveOperation.TryRentOperationForPopulation();
        }

        try
        {
            var operation = new TcpFrameReceiveOperation();
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
