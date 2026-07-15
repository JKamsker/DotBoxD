using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Debugging;

public enum SandboxDebugCheckpointKind
{
    FunctionEntry,
    Statement,
    Expression,
    Call,
    LoopIteration,
    FunctionExit,
    Exception
}

/// <summary>A debugger-visible stop candidate emitted by the IR interpreter.</summary>
public sealed record SandboxDebugCheckpoint
{
    public SandboxDebugCheckpoint(
        SandboxRunId runId,
        SandboxNodeDescriptor node,
        SandboxDebugCheckpointKind kind,
        ISandboxDebugFrame frame,
        SandboxValue? value = null,
        SandboxError? error = null)
    {
        RunId = runId ?? throw new ArgumentNullException(nameof(runId));
        Node = node ?? throw new ArgumentNullException(nameof(node));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported debug checkpoint kind.");
        }

        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        Kind = kind;
        Value = value;
        Error = error;
    }

    public SandboxRunId RunId { get; }

    public SandboxNodeDescriptor Node { get; }

    public SandboxDebugCheckpointKind Kind { get; }

    public ISandboxDebugFrame Frame { get; }

    public SandboxValue? Value { get; }

    public SandboxError? Error { get; }
}
