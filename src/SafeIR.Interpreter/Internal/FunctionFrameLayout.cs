namespace SafeIR.Interpreter.Internal;

using SafeIR;

/// <summary>
/// A function's local-variable shape resolved once: every parameter, assignment
/// target, and loop variable name is mapped to a stable integer slot. Frames can
/// then store locals in a flat <see cref="SandboxValue"/> array indexed by slot
/// instead of allocating a string-keyed dictionary per invocation.
/// </summary>
internal sealed class FunctionFrameLayout
{
    private readonly Dictionary<string, int> _slots;
    private readonly bool[] _i32Slots;

    private FunctionFrameLayout(string functionId, Dictionary<string, int> slots, bool[] i32Slots)
    {
        FunctionId = functionId;
        _slots = slots;
        _i32Slots = i32Slots;
        SlotCount = slots.Count;
        HasI32Slots = Array.IndexOf(i32Slots, true) >= 0;
    }

    public string FunctionId { get; }

    public int SlotCount { get; }

    public bool HasI32Slots { get; }

    public static FunctionFrameLayout Build(
        SandboxFunction function,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IBindingCatalog bindings)
    {
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);

        // Parameters bind first and in order so positional argument binding maps
        // straight onto the leading slots.
        for (var i = 0; i < function.Parameters.Count; i++) {
            Reserve(slots, function.Parameters[i].Name);
        }

        CollectStatements(function.Body, slots);
        return new FunctionFrameLayout(function.Id, slots, BuildI32Slots(function, functionAnalysis, bindings, slots));
    }

    public int GetSlot(string name)
        => _slots.TryGetValue(name, out var slot)
            ? slot
            : throw Unknown(name);

    public bool IsI32Slot(int slot) => _i32Slots[slot];

    public bool IsI32Slot(string name) => IsI32Slot(GetSlot(name));

    private static void CollectStatements(IReadOnlyList<Statement> statements, Dictionary<string, int> slots)
    {
        foreach (var statement in statements) {
            CollectStatement(statement, slots);
        }
    }

    private static void CollectStatement(Statement statement, Dictionary<string, int> slots)
    {
        switch (statement) {
            case AssignmentStatement assignment:
                Reserve(slots, assignment.Name);
                break;
            case IfStatement branch:
                CollectStatements(branch.Then, slots);
                CollectStatements(branch.Else, slots);
                break;
            case WhileStatement loop:
                CollectStatements(loop.Body, slots);
                break;
            case ForRangeStatement range:
                Reserve(slots, range.LocalName);
                CollectStatements(range.Body, slots);
                break;
        }
    }

    private static void Reserve(Dictionary<string, int> slots, string name)
    {
        if (!slots.ContainsKey(name)) {
            slots[name] = slots.Count;
        }
    }

    private static bool[] BuildI32Slots(
        SandboxFunction function,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IBindingCatalog bindings,
        Dictionary<string, int> slots)
    {
        var candidates = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var parameter in function.Parameters)
        {
            Observe(candidates, parameter.Name, IsI32(parameter.Type));
        }

        ScanLocalKinds(function.Body, function, functionAnalysis, bindings, candidates);
        var i32Slots = new bool[slots.Count];
        foreach (var pair in slots)
        {
            i32Slots[pair.Value] = candidates.TryGetValue(pair.Key, out var i32) && i32;
        }

        return i32Slots;
    }

    private static void ScanLocalKinds(
        IReadOnlyList<Statement> statements,
        SandboxFunction function,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IBindingCatalog bindings,
        Dictionary<string, bool> candidates)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case AssignmentStatement assignment:
                    Observe(candidates, assignment.Name, IsI32(InferType(assignment.Value, function, functionAnalysis, bindings, candidates)));
                    break;
                case IfStatement branch:
                    ScanLocalKinds(branch.Then, function, functionAnalysis, bindings, candidates);
                    ScanLocalKinds(branch.Else, function, functionAnalysis, bindings, candidates);
                    break;
                case ForRangeStatement range:
                    Observe(candidates, range.LocalName, i32: true);
                    ScanLocalKinds(range.Body, function, functionAnalysis, bindings, candidates);
                    break;
                case WhileStatement loop:
                    ScanLocalKinds(loop.Body, function, functionAnalysis, bindings, candidates);
                    break;
            }
        }
    }

    private static void Observe(Dictionary<string, bool> candidates, string name, bool i32)
    {
        if (!candidates.TryGetValue(name, out var existing))
        {
            candidates[name] = i32;
            return;
        }

        candidates[name] = existing && i32;
    }

    private static SandboxType? InferType(
        Expression expression,
        SandboxFunction function,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IBindingCatalog bindings,
        IReadOnlyDictionary<string, bool> candidates)
        => expression switch
        {
            LiteralExpression literal => literal.Value.Type,
            VariableExpression variable => candidates.TryGetValue(variable.Name, out var i32) && i32
                ? SandboxType.I32
                : ParameterType(function, variable.Name),
            UnaryExpression { Operator: "!" } => SandboxType.Bool,
            UnaryExpression unary => InferType(unary.Operand, function, functionAnalysis, bindings, candidates),
            BinaryExpression binary => binary.Operator is "&&" or "||" or "==" or "!=" or "<" or "<=" or ">" or ">="
                ? SandboxType.Bool
                : InferType(binary.Left, function, functionAnalysis, bindings, candidates),
            CallExpression call => InferCallType(call, functionAnalysis, bindings),
            _ => null
        };

    private static SandboxType? InferCallType(
        CallExpression call,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IBindingCatalog bindings)
        => call.Name == "list.count"
            ? SandboxType.I32
            : functionAnalysis.TryGetValue(call.Name, out var analysis)
                ? analysis.ReturnType
                : bindings.TryGet(call.Name, out var binding) ? binding.ReturnType : null;

    private static SandboxType? ParameterType(SandboxFunction function, string name)
    {
        foreach (var parameter in function.Parameters)
        {
            if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
            {
                return parameter.Type;
            }
        }

        return null;
    }

    private static bool IsI32(SandboxType? type) => type is { Name: "I32" };

    private static SandboxRuntimeException Unknown(string name)
        => new(new SandboxError(SandboxErrorCode.ValidationError, $"unknown local '{name}' at runtime"));
}
