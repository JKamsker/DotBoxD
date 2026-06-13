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

    private FunctionFrameLayout(string functionId, Dictionary<string, int> slots)
    {
        FunctionId = functionId;
        _slots = slots;
        SlotCount = slots.Count;
    }

    public string FunctionId { get; }

    public int SlotCount { get; }

    public static FunctionFrameLayout Build(SandboxFunction function)
    {
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);

        // Parameters bind first and in order so positional argument binding maps
        // straight onto the leading slots.
        for (var i = 0; i < function.Parameters.Count; i++) {
            Reserve(slots, function.Parameters[i].Name);
        }

        CollectStatements(function.Body, slots);
        return new FunctionFrameLayout(function.Id, slots);
    }

    public int GetSlot(string name)
        => _slots.TryGetValue(name, out var slot)
            ? slot
            : throw Unknown(name);

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

    private static SandboxRuntimeException Unknown(string name)
        => new(new SandboxError(SandboxErrorCode.ValidationError, $"unknown local '{name}' at runtime"));
}
