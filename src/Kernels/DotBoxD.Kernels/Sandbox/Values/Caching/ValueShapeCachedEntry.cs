namespace DotBoxD.Kernels.Sandbox.Values;

internal class ValueShapeCachedEntry(ShapeInfo info)
{
    public ShapeInfo Info { get; } = info;

    public virtual bool TryGet(SandboxValue value, out ShapeInfo cached)
    {
        cached = Info;
        return true;
    }
}

internal sealed class VersionedValueShapeCachedEntry(
    ShapeInfo info,
    ValueShapeGeneration generation) : ValueShapeCachedEntry(info)
{
    public override bool TryGet(SandboxValue value, out ShapeInfo cached)
    {
        if (value is ListValue list && list.ShapeGeneration == generation)
        {
            cached = Info;
            return true;
        }

        cached = default;
        return false;
    }
}

internal readonly record struct ValueShapeGeneration(int Version, long Epoch);

// The per-list generation fits in existing ListValue padding. The process epoch changes only if that
// counter wraps, preventing a decades-old hot or persistent entry from becoming valid again.
internal static class ListValueShapeGeneration
{
    private static long s_epoch;

    public static long Epoch => Volatile.Read(ref s_epoch);

    public static void AdvanceEpoch() =>
        Interlocked.Increment(ref s_epoch);
}
