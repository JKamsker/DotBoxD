using System.Runtime.CompilerServices;

namespace DotBoxD.Transports.Tcp;

/// <summary>Selects shared, connection-local, or task-backed TCP receive completion.</summary>
internal static class TcpFrameReceiveOperationAcquisition
{
    private static readonly ConditionalWeakTable<TcpConnection, TcpFrameReceiveOperationCache> Caches = new();

    public static bool MustUseFallback(TcpConnection connection) =>
        TcpFrameReceiveOperationPopulation.IsAtCapacity &&
        TcpFrameReceiveOperationPopulation.HasNoAvailableOperation() &&
        !CanAcquireDedicatedOperation(connection);

    public static TcpFrameReceiveOperation? Rent(TcpConnection connection)
    {
        var operation = TcpFrameReceiveOperation.TryRentOperationForPopulation() ??
                        TcpFrameReceiveOperationPopulation.CreateOrRentRaced();
        return operation ??
               TryRentDedicatedOperation(connection) ??
               TryCreateDedicatedOperation(connection);
    }

    public static void ObserveSuccessfulFallback(TcpConnection connection)
    {
        if (connection.IsDisposedForReceive)
        {
            return;
        }

        try
        {
            var cache = Caches.GetValue(
                connection,
                static _ => new TcpFrameReceiveOperationCache());
            if (connection.IsDisposedForReceive && Caches.Remove(connection))
            {
                cache.Dispose();
            }
        }
        catch
        {
            // Admission is a performance hint; it must not replace a successfully received frame.
        }
    }

    public static bool HasDedicatedOperation(TcpConnection connection) =>
        GetDedicatedOperationCount(connection) != 0;

    public static bool HasDedicatedCache(TcpConnection connection) =>
        Caches.TryGetValue(connection, out _);

    public static int GetDedicatedOperationCount(TcpConnection connection) =>
        Caches.TryGetValue(connection, out var cache) ? cache.CreatedCount : 0;

    public static int GetAvailableDedicatedOperationCount(TcpConnection connection) =>
        Caches.TryGetValue(connection, out var cache) ? cache.AvailableCount : 0;

    public static void Remove(TcpConnection connection)
    {
        if (Caches.TryGetValue(connection, out var cache) && Caches.Remove(connection))
        {
            cache.Dispose();
        }
    }

    private static bool CanAcquireDedicatedOperation(TcpConnection connection) =>
        Caches.TryGetValue(connection, out var cache) && cache.CanAcquire;

    private static TcpFrameReceiveOperation? TryRentDedicatedOperation(TcpConnection connection)
    {
        if (!Caches.TryGetValue(connection, out var cache))
        {
            return null;
        }

        var operation = cache.TryRent();
        operation?.ReturnTo(cache);
        return operation;
    }

    private static TcpFrameReceiveOperation? TryCreateDedicatedOperation(TcpConnection connection)
    {
        if (!Caches.TryGetValue(connection, out var cache))
        {
            return null;
        }

        var operation = cache.TryCreate();
        operation?.ReturnTo(cache);
        return operation;
    }
}
