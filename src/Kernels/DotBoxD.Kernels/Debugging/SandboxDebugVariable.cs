using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Debugging;

public enum SandboxDebugVariableKind
{
    Argument,
    Local
}

/// <summary>A typed argument or local exposed by an interpreter debug frame.</summary>
public sealed record SandboxDebugVariable
{
    public SandboxDebugVariable(
        string name,
        SandboxType type,
        SandboxDebugVariableKind kind,
        bool isAssigned,
        SandboxValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Type = type ?? throw new ArgumentNullException(nameof(type));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported debug variable kind.");
        }

        if (isAssigned != (value is not null))
        {
            throw new ArgumentException("Assigned variables require a value and unassigned variables cannot have one.", nameof(value));
        }

        if (value is not null && !type.Equals(value.Type))
        {
            throw new ArgumentException("The variable value must match its declared sandbox type.", nameof(value));
        }

        Kind = kind;
        IsAssigned = isAssigned;
        Value = value;
    }

    public string Name { get; }

    public SandboxType Type { get; }

    public SandboxDebugVariableKind Kind { get; }

    public bool IsAssigned { get; }

    public SandboxValue? Value { get; }
}
