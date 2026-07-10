namespace DotBoxD.Kernels.Debugging;

/// <summary>Maps an IR slot to the name shown in source-level debugger windows.</summary>
public sealed record KernelDebugVariableBinding
{
    public KernelDebugVariableBinding(
        string functionId,
        string slotName,
        string sourceName,
        SandboxNodeId? scopeStart = null,
        SandboxNodeId? scopeEnd = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        FunctionId = functionId;
        SlotName = slotName;
        SourceName = sourceName;
        ScopeStart = scopeStart;
        ScopeEnd = scopeEnd;
    }

    public string FunctionId { get; }

    public string SlotName { get; }

    public string SourceName { get; }

    public SandboxNodeId? ScopeStart { get; }

    public SandboxNodeId? ScopeEnd { get; }
}

