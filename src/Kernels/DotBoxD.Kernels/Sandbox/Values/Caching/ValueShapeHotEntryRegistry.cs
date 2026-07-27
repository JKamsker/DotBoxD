namespace DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// Bounded cross-thread discovery for current per-thread shape entries. Registry slots weakly reference
/// their owners, so terminated or otherwise collected producer entries can be replaced.
/// </summary>
internal static class ValueShapeHotEntryRegistry
{
    internal const int Capacity = 16;
    internal const int DirectoryCapacity = 256;

    private static readonly ValueShapeHotEntryTable Entries = new(Capacity, DirectoryCapacity);

    public static bool TryRegister(ValueShapeHotEntry entry) => Entries.TryRegister(entry);

    public static void Publish(ValueShapeHotEntry entry, SandboxValue value) =>
        Entries.PublishIfRequested(entry, value);

    public static bool TryGet(
        SandboxValue value,
        ValueShapeGeneration generation,
        ValueShapeHotEntry? localEntry,
        out ShapeInfo info)
        => Entries.TryGet(value, generation, localEntry, out info);
}
