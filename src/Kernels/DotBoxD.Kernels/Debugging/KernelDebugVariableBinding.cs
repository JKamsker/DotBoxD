namespace DotBoxD.Kernels.Debugging;

/// <summary>Maps an IR slot to the name shown in source-level debugger windows.</summary>
public sealed record KernelDebugVariableBinding
{
    public KernelDebugVariableBinding(
        string functionId,
        string slotName,
        string sourceName,
        SandboxNodeId? scopeStart = null,
        SandboxNodeId? scopeEnd = null,
        string? typeName = null,
        string? displayValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (typeName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        }

        if (displayValue is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(displayValue);
        }
        FunctionId = functionId;
        SlotName = slotName;
        SourceName = sourceName;
        ScopeStart = scopeStart;
        ScopeEnd = scopeEnd;
        TypeName = typeName;
        DisplayValue = displayValue;
    }

    public string FunctionId { get; }

    public string SlotName { get; }

    public string SourceName { get; }

    public SandboxNodeId? ScopeStart { get; }

    public SandboxNodeId? ScopeEnd { get; }

    /// <summary>Optional authored type name used for synthetic values and IDE presentation.</summary>
    public string? TypeName { get; }

    /// <summary>Optional display-only value for an authored variable that has no runtime sandbox slot.</summary>
    public string? DisplayValue { get; }
}
