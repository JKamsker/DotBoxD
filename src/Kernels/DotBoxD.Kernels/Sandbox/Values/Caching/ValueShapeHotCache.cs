namespace DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// Keeps the most recently composed shape on each thread without retaining the value or allocating a
/// per-result cache box. A weak target bounds retention, and mutable owned lists carry a generation so a
/// reset performed on another thread cannot revive an old shape.
/// </summary>
internal static class ValueShapeHotCache
{
    [ThreadStatic]
    private static Entry? t_entry;

    public static bool TryGet(SandboxValue value, out ShapeInfo info)
    {
        var entry = t_entry;
        if (entry is not null && entry.TryGet(value, GetGeneration(value), out info))
        {
            return true;
        }

        info = default;
        return false;
    }

    public static void Set(SandboxValue value, ShapeInfo info)
    {
        var generation = GetGeneration(value);
        if (t_entry is { } entry)
        {
            entry.Set(value, generation, info);
            return;
        }

        t_entry = new Entry(value, generation, info);
    }

    private static ValueShapeGeneration GetGeneration(SandboxValue value) =>
        value is ListValue list ? list.ShapeGeneration : default;

    private sealed class Entry
    {
        private readonly WeakReference<SandboxValue> _value;
        private ShapeInfo _info;
        private ValueShapeGeneration _generation;

        public Entry(
            SandboxValue value,
            ValueShapeGeneration generation,
            ShapeInfo info)
        {
            _value = new WeakReference<SandboxValue>(value);
            _generation = generation;
            _info = info;
        }

        public bool TryGet(
            SandboxValue value,
            ValueShapeGeneration generation,
            out ShapeInfo info)
        {
            if (_generation == generation &&
                _value.TryGetTarget(out var cached) &&
                ReferenceEquals(cached, value))
            {
                info = _info;
                return true;
            }

            info = default;
            return false;
        }

        public void Set(
            SandboxValue value,
            ValueShapeGeneration generation,
            ShapeInfo info)
        {
            _info = info;
            _generation = generation;
            _value.SetTarget(value);
        }
    }
}
