using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Interpreter.Debugging;

internal sealed class InterpreterDebugFrame : ISandboxDebugFrame
{
    private readonly InterpreterFrame _frame;
    private readonly FunctionFrameLayout _layout;
    private readonly ResourceLimits _limits;

    public InterpreterDebugFrame(
        InterpreterFrame frame,
        FunctionFrameLayout layout,
        ResourceLimits limits,
        InterpreterDebugFrame? caller)
    {
        _frame = frame;
        _layout = layout;
        _limits = limits;
        Caller = caller;
        Depth = caller is null ? 0 : caller.Depth + 1;
    }

    public string FunctionId => _layout.FunctionId;

    public int Depth { get; }

    public ISandboxDebugFrame? Caller { get; }

    public IReadOnlyList<SandboxDebugVariable> Arguments => SnapshotVariables(arguments: true);

    public IReadOnlyList<SandboxDebugVariable> Locals => SnapshotVariables(arguments: false);

    public bool TrySetVariable(string name, SandboxValue value, out SandboxError? error)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (!_layout.TryGetSlot(name, out var slot))
        {
            error = Invalid($"unknown debug variable '{name}'");
            return false;
        }

        return TryWrite(slot, value, out error);
    }

    public bool TrySetMember(
        string name,
        IReadOnlyList<SandboxDebugValuePathSegment> path,
        SandboxValue value,
        out SandboxError? error)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(value);
        if (!_layout.TryGetSlot(name, out var slot) || !_frame.IsSlotAssigned(slot))
        {
            error = Invalid($"debug variable '{name}' is unavailable");
            return false;
        }

        if (!SandboxDebugValueReplacer.TryReplace(_frame.Read(name), path, value, out var replacement, out error))
        {
            return false;
        }

        return TryWrite(slot, replacement, out error);
    }

    private IReadOnlyList<SandboxDebugVariable> SnapshotVariables(bool arguments)
    {
        var start = arguments ? 0 : _layout.ParameterCount;
        var end = arguments ? _layout.ParameterCount : _layout.SlotCount;
        var variables = new SandboxDebugVariable[end - start];
        for (var slot = start; slot < end; slot++)
        {
            var assigned = _frame.IsSlotAssigned(slot);
            var value = assigned ? _frame.Read(_layout.GetName(slot)) : null;
            var type = _layout.GetType(slot) ?? value?.Type ?? SandboxType.Unit;
            variables[slot - start] = new SandboxDebugVariable(
                _layout.GetName(slot),
                type,
                arguments ? SandboxDebugVariableKind.Argument : SandboxDebugVariableKind.Local,
                assigned,
                value);
        }

        return variables;
    }

    private bool TryWrite(int slot, SandboxValue value, out SandboxError? error)
    {
        var expectedType = _layout.GetType(slot);
        if (expectedType is null && _frame.IsSlotAssigned(slot))
        {
            expectedType = _frame.Read(_layout.GetName(slot)).Type;
        }

        if (expectedType is null || !expectedType.Equals(value.Type))
        {
            error = Invalid("debug write does not match the original slot type");
            return false;
        }

        try
        {
            SandboxValueValidator.RequireType(value, expectedType, "debug write type mismatch");
            new ResourceMeter(_limits).ChargeValue(value);
            _frame.Write(_layout.GetName(slot), value);
            error = null;
            return true;
        }
        catch (SandboxRuntimeException exception)
        {
            error = exception.Error;
            return false;
        }
    }

    private static SandboxError Invalid(string message) => new(SandboxErrorCode.InvalidInput, message);
}

internal static class SandboxDebugValueReplacer
{
    public static bool TryReplace(
        SandboxValue root,
        IReadOnlyList<SandboxDebugValuePathSegment> path,
        SandboxValue replacement,
        out SandboxValue value,
        out SandboxError? error)
    {
        try
        {
            value = Replace(root, path, 0, replacement);
            error = null;
            return true;
        }
        catch (SandboxRuntimeException exception)
        {
            value = root;
            error = exception.Error;
            return false;
        }
    }

    private static SandboxValue Replace(
        SandboxValue current,
        IReadOnlyList<SandboxDebugValuePathSegment> path,
        int depth,
        SandboxValue replacement)
    {
        if (depth == path.Count)
        {
            return replacement;
        }

        return path[depth] switch
        {
            SandboxDebugListIndex index when current is ListValue list => ReplaceList(list, index.Index, path, depth, replacement),
            SandboxDebugRecordField field when current is RecordValue record => ReplaceRecord(record, field.Index, path, depth, replacement),
            SandboxDebugMapValue mapValue when current is MapValue map => ReplaceMap(map, mapValue.Key, path, depth, replacement),
            _ => throw Invalid("debug member path does not match the sandbox value")
        };
    }

    private static SandboxValue ReplaceList(
        ListValue list,
        int index,
        IReadOnlyList<SandboxDebugValuePathSegment> path,
        int depth,
        SandboxValue replacement)
    {
        if (index < 0 || index >= list.Count)
        {
            throw Invalid("debug list index is out of range");
        }

        var values = list.Values.ToArray();
        values[index] = Replace(values[index], path, depth + 1, replacement);
        return SandboxValue.FromList(values, list.ItemType);
    }

    private static SandboxValue ReplaceRecord(
        RecordValue record,
        int index,
        IReadOnlyList<SandboxDebugValuePathSegment> path,
        int depth,
        SandboxValue replacement)
    {
        if (index < 0 || index >= record.Fields.Count)
        {
            throw Invalid("debug record field is out of range");
        }

        var fields = record.Fields.ToArray();
        fields[index] = Replace(fields[index], path, depth + 1, replacement);
        return SandboxValue.FromRecord(fields);
    }

    private static SandboxValue ReplaceMap(
        MapValue map,
        SandboxValue key,
        IReadOnlyList<SandboxDebugValuePathSegment> path,
        int depth,
        SandboxValue replacement)
    {
        if (!map.Values.TryGetValue(key, out var existing))
        {
            throw Invalid("debug map key was not found");
        }

        var values = new Dictionary<SandboxValue, SandboxValue>(map.Values);
        values[key] = Replace(existing, path, depth + 1, replacement);
        return SandboxValue.FromMap(values, map.KeyType, map.ValueType);
    }

    private static SandboxRuntimeException Invalid(string message)
        => new(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
