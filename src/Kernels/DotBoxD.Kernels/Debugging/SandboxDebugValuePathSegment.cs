using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Debugging;

/// <summary>A safe navigation step used to replace a nested sandbox value.</summary>
public abstract record SandboxDebugValuePathSegment;

public sealed record SandboxDebugListIndex : SandboxDebugValuePathSegment
{
    public SandboxDebugListIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Index = index;
    }

    public int Index { get; }
}

public sealed record SandboxDebugRecordField : SandboxDebugValuePathSegment
{
    public SandboxDebugRecordField(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Index = index;
    }

    public int Index { get; }
}

public sealed record SandboxDebugMapValue : SandboxDebugValuePathSegment
{
    public SandboxDebugMapValue(SandboxValue key)
        => Key = key ?? throw new ArgumentNullException(nameof(key));

    public SandboxValue Key { get; }
}

