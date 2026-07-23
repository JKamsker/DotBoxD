namespace DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// Keeps the most recently composed shape on each thread without retaining the value or allocating a
/// per-result cache box. Same-thread hits remain lock-free. A bounded weak registry lets another thread
/// discover the producer's current entry and promote it locally; ordinary misses still use the persistent
/// cache and full measurement. Mutable owned lists carry a generation so reset cannot revive an old shape.
/// </summary>
internal static class ValueShapeHotCache
{
    private const int MinimumPublishedListElements = 16;
    private const int MinimumPublishedMapEntries = 8;

    [ThreadStatic]
    private static ValueShapeHotEntry? t_entry;

    public static bool TryGet(SandboxValue value, out ShapeInfo info)
    {
        var entry = t_entry;
        if (entry is not null && entry.TryGetLocal(value, GetGeneration(value), out info))
        {
            return true;
        }

        info = default;
        return false;
    }

    public static bool TryGetPublished(SandboxValue value, out ShapeInfo info)
    {
        if (ShouldPublishCrossThread(value))
        {
            return ValueShapeHotEntryRegistry.TryGet(value, GetGeneration(value), t_entry, out info);
        }

        info = default;
        return false;
    }

    public static void Set(SandboxValue value, ShapeInfo info)
    {
        var generation = GetGeneration(value);
        if (t_entry is { } entry)
        {
            entry.SetLocal(value, generation, info);
            return;
        }

        entry = new ValueShapeHotEntry(value, generation, info);
        t_entry = entry;
    }

    public static void Publish(SandboxValue value, ShapeInfo info)
    {
        var generation = GetGeneration(value);
        var entry = t_entry;
        if (entry is null)
        {
            entry = new ValueShapeHotEntry(value, generation, info);
            t_entry = entry;
        }

        entry.Publish(value, generation, info);
        if (!ShouldPublishCrossThread(value))
        {
            return;
        }

        if (entry.TryBeginRegistration())
        {
            ValueShapeHotEntryRegistry.TryRegister(entry);
        }

        ValueShapeHotEntryRegistry.Publish(entry, value);
    }

    private static ValueShapeGeneration GetGeneration(SandboxValue value) =>
        value is ListValue list ? list.ShapeGeneration : default;

    private static bool ShouldPublishCrossThread(SandboxValue value) => value switch
    {
        ListValue list => list.Values.Count >= MinimumPublishedListElements,
        MapValue map => map.Values.Count >= MinimumPublishedMapEntries,
        _ => false,
    };
}
