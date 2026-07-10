using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Debugging;

/// <summary>A logical interpreter call frame containing only sandbox values.</summary>
public interface ISandboxDebugFrame
{
    string FunctionId { get; }

    int Depth { get; }

    ISandboxDebugFrame? Caller { get; }

    IReadOnlyList<SandboxDebugVariable> Arguments { get; }

    IReadOnlyList<SandboxDebugVariable> Locals { get; }

    bool TrySetVariable(string name, SandboxValue value, out SandboxError? error);

    bool TrySetMember(
        string name,
        IReadOnlyList<SandboxDebugValuePathSegment> path,
        SandboxValue value,
        out SandboxError? error);
}

