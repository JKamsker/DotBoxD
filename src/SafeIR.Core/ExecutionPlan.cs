namespace SafeIR;

public sealed record FunctionAnalysis(SandboxType ReturnType, SandboxEffect Effects);

public enum BytecodeOp
{
    LoadConst,
    LoadLocal,
    StoreLocal,
    Pop,
    Unary,
    Binary,
    Jump,
    JumpIfFalse,
    CallBinding,
    CallFunction,
    ListEmpty,
    ListOf,
    ListCount,
    ListGet,
    ListAdd,
    MapEmpty,
    MapContainsKey,
    MapGet,
    MapSet,
    MapRemove,
    Return
}

public sealed record BytecodeInstruction(BytecodeOp Op, object? Operand = null);

public sealed record BytecodeCall(string Id, int ArgumentCount);

public sealed record BytecodeFunction(
    string Id,
    bool IsEntrypoint,
    IReadOnlyList<Parameter> Parameters,
    SandboxType ReturnType,
    IReadOnlyList<BytecodeInstruction> Instructions);

public sealed record ExecutableBytecode(IReadOnlyDictionary<string, BytecodeFunction> Functions);

public sealed record ExecutionPlan(
    string ModuleHash,
    string PlanHash,
    string PolicyHash,
    string BindingManifestHash,
    SandboxModule Module,
    SandboxPolicy Policy,
    BindingRegistry Bindings,
    ResourceLimits Budget,
    ExecutableBytecode Bytecode,
    IReadOnlyDictionary<string, FunctionAnalysis> FunctionAnalysis);

public sealed record SandboxExecutionOptions
{
    public ExecutionMode Mode { get; init; } = ExecutionMode.Auto;
    public bool EnableDebugTrace { get; init; }
    public bool AllowFallbackToInterpreter { get; init; } = true;
    public bool RequireDeterministic { get; init; }
    public SandboxRunId? RunId { get; init; }
}

public enum ExecutionMode
{
    Interpreted,
    Compiled,
    Auto
}

public sealed record SandboxExecutionResult
{
    public bool Succeeded { get; init; }
    public SandboxValue? Value { get; init; }
    public SandboxError? Error { get; init; }
    public required SandboxResourceUsage ResourceUsage { get; init; }
    public required IReadOnlyList<SandboxAuditEvent> AuditEvents { get; init; }
    public ExecutionMode ActualMode { get; init; }
    public required string ModuleHash { get; init; }
    public required string PlanHash { get; init; }
    public required string PolicyHash { get; init; }
    public string? ArtifactHash { get; init; }
}
