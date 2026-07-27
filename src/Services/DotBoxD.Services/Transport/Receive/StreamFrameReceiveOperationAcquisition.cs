using System.Runtime.CompilerServices;

namespace DotBoxD.Services.Transport;

/// <summary>Selects shared, connection-local, or task-backed Stream receive completion.</summary>
internal static class StreamFrameReceiveOperationAcquisition
{
    private static readonly ConditionalWeakTable<StreamConnection, StreamFrameReceiveOperationCache> Caches =
        new();

    public static bool CanAcquireDedicatedOperation(StreamConnection connection) =>
        Caches.TryGetValue(connection, out var cache) && cache.CanAcquire;

    public static StreamFrameReceiveOperation? RentDedicated(StreamConnection connection) =>
        TryRentDedicatedOperation(connection) ?? TryCreateDedicatedOperation(connection);

    public static bool TryReturn(
        StreamFrameReceiveOperation operation,
        ref StreamFrameReceiveOperationCache? cache)
    {
        var returnCache = cache;
        cache = null;
        if (returnCache is null)
        {
            return false;
        }

        returnCache.Return(operation);
        return true;
    }

    public static void ObserveSuccessfulFallback(StreamConnection connection)
    {
        if (connection.IsDisposedForReceive)
        {
            return;
        }

        try
        {
            var cache = Caches.GetValue(
                connection,
                static _ => new StreamFrameReceiveOperationCache());
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

    public static bool HasDedicatedOperation(StreamConnection connection) =>
        GetDedicatedOperationCount(connection) != 0;

    public static bool HasDedicatedCache(StreamConnection connection) =>
        Caches.TryGetValue(connection, out _);

    public static int GetDedicatedOperationCount(StreamConnection connection) =>
        Caches.TryGetValue(connection, out var cache) ? cache.CreatedCount : 0;

    public static int GetAvailableDedicatedOperationCount(StreamConnection connection) =>
        Caches.TryGetValue(connection, out var cache) ? cache.AvailableCount : 0;

    public static void Remove(StreamConnection connection)
    {
        if (Caches.TryGetValue(connection, out var cache) && Caches.Remove(connection))
        {
            cache.Dispose();
        }
    }

    private static StreamFrameReceiveOperation? TryRentDedicatedOperation(StreamConnection connection)
    {
        if (!Caches.TryGetValue(connection, out var cache))
        {
            return null;
        }

        var operation = cache.TryRent();
        operation?.ReturnTo(cache);
        return operation;
    }

    private static StreamFrameReceiveOperation? TryCreateDedicatedOperation(StreamConnection connection)
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
