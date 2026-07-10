using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Debugging;

public enum SandboxNodeKind
{
    Function,
    Statement,
    Expression
}

/// <summary>Describes the stable structural position represented by a sandbox node ID.</summary>
public sealed record SandboxNodeDescriptor
{
    public SandboxNodeDescriptor(
        SandboxNodeId id,
        string functionId,
        SandboxNodeKind kind,
        string structuralPath,
        SourceSpan? sourceSpan)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(functionId);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported sandbox node kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(structuralPath);
        FunctionId = functionId;
        Kind = kind;
        StructuralPath = structuralPath;
        SourceSpan = sourceSpan;
    }

    public SandboxNodeId Id { get; }

    public string FunctionId { get; }

    public SandboxNodeKind Kind { get; }

    public string StructuralPath { get; }

    public SourceSpan? SourceSpan { get; }
}
