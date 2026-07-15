using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

/// <summary>
/// A function's local-variable shape resolved once: every parameter, assignment
/// target, and loop variable name is mapped to a stable integer slot. Frames can
/// then store locals in a flat <see cref="SandboxValue"/> array indexed by slot
/// instead of allocating a string-keyed dictionary per invocation.
/// </summary>
internal sealed class FunctionFrameLayout
{
    private readonly Dictionary<string, int> _slots;
    private readonly string[] _slotNames;
    private readonly SandboxType?[] _slotTypes;
    private readonly SlotKind[] _slotKinds;

    private FunctionFrameLayout(
        string functionId,
        int parameterCount,
        Dictionary<string, int> slots,
        SandboxType?[] slotTypes)
    {
        FunctionId = functionId;
        ParameterCount = parameterCount;
        _slots = slots;
        _slotNames = BuildSlotNames(slots);
        _slotTypes = slotTypes;
        _slotKinds = slotTypes.Select(KindOf).ToArray();
        SlotCount = slots.Count;
        RequiresRawAssignmentState =
            Array.IndexOf(_slotKinds, SlotKind.I32, parameterCount) >= 0 ||
            Array.IndexOf(_slotKinds, SlotKind.I64, parameterCount) >= 0 ||
            Array.IndexOf(_slotKinds, SlotKind.F64, parameterCount) >= 0;
        HasBoxedSlots = Array.IndexOf(_slotKinds, SlotKind.Boxed) >= 0;
        HasI32Slots = Array.IndexOf(_slotKinds, SlotKind.I32) >= 0;
        HasI64Slots = Array.IndexOf(_slotKinds, SlotKind.I64) >= 0;
        HasF64Slots = Array.IndexOf(_slotKinds, SlotKind.F64) >= 0;
    }

    public string FunctionId { get; }

    public int SlotCount { get; }

    public int ParameterCount { get; }

    public bool RequiresRawAssignmentState { get; }

    public bool HasBoxedSlots { get; }

    public bool HasI32Slots { get; }

    public bool HasI64Slots { get; }

    public bool HasF64Slots { get; }

    public bool HasRawSlots => HasI32Slots || HasI64Slots || HasF64Slots;

    public static FunctionFrameLayout Build(
        SandboxFunction function,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IBindingCatalog bindings)
    {
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);

        // Parameters bind first and in order so positional argument binding maps
        // straight onto the leading slots.
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            Reserve(slots, function.Parameters[i].Name);
        }

        CollectStatements(function.Body, slots);
        var slotTypes = FunctionFrameTypeResolver.Resolve(function, functionAnalysis, bindings, slots);
        return new FunctionFrameLayout(function.Id, function.Parameters.Count, slots, slotTypes);
    }

    public int GetSlot(string name)
        => _slots.TryGetValue(name, out var slot)
            ? slot
            : throw Unknown(name);

    public bool TryGetSlot(string name, out int slot) => _slots.TryGetValue(name, out slot);

    public string GetName(int slot) => _slotNames[slot];

    public SandboxType? GetType(int slot) => _slotTypes[slot];

    public bool IsArgument(int slot) => slot < ParameterCount;

    public bool IsI32Slot(int slot) => _slotKinds[slot] == SlotKind.I32;

    public bool IsI32Slot(string name) => IsI32Slot(GetSlot(name));

    public bool IsF64Slot(int slot) => _slotKinds[slot] == SlotKind.F64;

    public bool IsF64Slot(string name) => IsF64Slot(GetSlot(name));

    public bool IsI64Slot(int slot) => _slotKinds[slot] == SlotKind.I64;

    public bool IsBoxedSlot(int slot) => _slotKinds[slot] == SlotKind.Boxed;

    private static void CollectStatements(IReadOnlyList<Statement> statements, Dictionary<string, int> slots)
    {
        foreach (var statement in statements)
        {
            CollectStatement(statement, slots);
        }
    }

    private static void CollectStatement(Statement statement, Dictionary<string, int> slots)
    {
        switch (statement)
        {
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
        if (!slots.ContainsKey(name))
        {
            slots[name] = slots.Count;
        }
    }

    private static SlotKind KindOf(SandboxType? type)
        => type switch
        {
            { Name: "I32" } => SlotKind.I32,
            { Name: "I64" } => SlotKind.I64,
            { Name: "F64" } => SlotKind.F64,
            _ => SlotKind.Boxed
        };

    private static string[] BuildSlotNames(Dictionary<string, int> slots)
    {
        var names = new string[slots.Count];
        foreach (var pair in slots)
        {
            names[pair.Value] = pair.Key;
        }

        return names;
    }

    private static SandboxRuntimeException Unknown(string name)
        => new(new SandboxError(SandboxErrorCode.ValidationError, $"unknown local '{name}' at runtime"));

    private enum SlotKind
    {
        Boxed,
        I32,
        I64,
        F64
    }
}
