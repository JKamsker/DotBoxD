using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
namespace DotBoxD.Kernels.Interpreter.Frame;

internal sealed partial class InterpreterFrame
{
    private readonly FunctionFrameLayout _layout;
    private readonly SandboxValue?[] _slots;
    private readonly int[] _i32Slots;
    private readonly long[] _i64Slots;
    private readonly double[] _f64Slots;
    private readonly bool[] _assigned;
    internal InterpreterFrame(
        FunctionFrameLayout layout,
        SandboxValue?[] slots,
        int[] i32Slots,
        long[] i64Slots,
        double[] f64Slots,
        bool[] assigned)
    {
        _layout = layout;
        _slots = slots;
        _i32Slots = i32Slots;
        _i64Slots = i64Slots;
        _f64Slots = f64Slots;
        _assigned = assigned;
    }
    public string FunctionId => _layout.FunctionId;
    internal FunctionFrameLayout Layout => _layout;
    public int GetSlot(string name) => _layout.GetSlot(name);
    public SandboxValue Read(string name)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI32Slot(slot))
        {
            return RawSlotAssignmentState.IsAssigned(_assigned, slot)
                ? SandboxValue.FromInt32(_i32Slots[slot])
                : throw Unassigned(name);
        }
        if (_layout.IsF64Slot(slot))
        {
            return RawSlotAssignmentState.IsAssigned(_assigned, slot)
                ? SandboxValue.FromDouble(_f64Slots[slot])
                : throw Unassigned(name);
        }
        if (_layout.IsI64Slot(slot))
        {
            return RawSlotAssignmentState.IsAssigned(_assigned, slot)
                ? SandboxValue.FromInt64(_i64Slots[slot])
                : throw Unassigned(name);
        }
        return TryGetBoxedValue<SandboxValue>(slot, out var value)
            ? value
            : throw Unassigned(name);
    }
    public void Write(string name, SandboxValue value)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI32Slot(slot))
        {
            _i32Slots[slot] = ((I32Value)value).Value;
        }
        else if (_layout.IsF64Slot(slot))
        {
            _f64Slots[slot] = ((F64Value)value).Value;
        }
        else if (_layout.IsI64Slot(slot))
        {
            _i64Slots[slot] = ((I64Value)value).Value;
        }
        else
        {
            WriteBoxedValue(slot, value);
        }
        if (_layout.HasRawSlots)
        {
            RawSlotAssignmentState.MarkAssigned(_assigned, slot);
        }
    }
    public bool CanReadInt32(string name)
    {
        var slot = _layout.GetSlot(name);
        return _layout.IsI32Slot(slot)
            ? RawSlotAssignmentState.IsAssigned(_assigned, slot)
            : TryGetBoxedValue<I32Value>(slot, out _);
    }
    public bool IsInt32Local(string name) => _layout.IsI32Slot(name);
    public bool IsInt32Slot(int slot) => _layout.IsI32Slot(slot);
    public bool IsF64Slot(int slot) => _layout.IsF64Slot(slot);
    public bool IsF64Slot(string name) => _layout.IsF64Slot(name);
    public bool IsSlotAssigned(int slot)
        => _layout.IsBoxedSlot(slot)
            ? _slots[slot] is not null
            : RawSlotAssignmentState.IsAssigned(_assigned, slot);
    public int ReadInt32(string name)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI32Slot(slot))
        {
            return RawSlotAssignmentState.IsAssigned(_assigned, slot) ? _i32Slots[slot] : throw Unassigned(name);
        }
        return TryGetBoxedValue<I32Value>(slot, out var value) ? value.Value : throw Unassigned(name);
    }
    public int ReadInt32Slot(int slot)
        => _layout.IsI32Slot(slot)
            ? RawSlotAssignmentState.IsAssigned(_assigned, slot) ? _i32Slots[slot] : throw UnassignedSlot()
            : TryGetBoxedValue<I32Value>(slot, out var value) ? value.Value : throw UnassignedSlot();
    public int ReadRawInt32Slot(int slot) => _i32Slots[slot];
    public bool TryGetStringSlot(string name, out int slot)
    {
        slot = _layout.GetSlot(name);
        return TryGetBoxedValue<StringValue>(slot, out _);
    }
    public int ReadStringLengthSlot(int slot)
        => ReadBoxedValue<StringValue>(slot).Value.Length;
    public bool TryGetListSlot(string name, out int slot)
    {
        slot = _layout.GetSlot(name);
        return TryGetBoxedValue<ListValue>(slot, out _);
    }
    public int ReadListCountSlot(int slot)
        => ReadBoxedValue<ListValue>(slot).Values.Count;
    public int ReadListInt32ItemSlot(int slot, int index)
    {
        var list = ReadBoxedValue<ListValue>(slot);
        var values = list.Values;
        if (index < 0 || index >= values.Count)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "list index is out of range"));
        }
        return values[index] is I32Value item
            ? item.Value
            : throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "expected I32 value"));
    }
    public bool TryReadListInt32ItemsSlot(int slot, out int[] items)
    {
        if (!TryGetBoxedValue<ListValue>(slot, out var list))
        {
            items = [];
            return false;
        }
        var values = list.Values;
        items = new int[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not I32Value item)
            {
                items = [];
                return false;
            }
            items[i] = item.Value;
        }
        return true;
    }
    public bool TryGetMapSlot(string name, out int slot)
    {
        slot = _layout.GetSlot(name);
        return TryGetBoxedValue<MapValue>(slot, out _);
    }
    public int ReadMapCountSlot(int slot)
        => ReadBoxedValue<MapValue>(slot).Values.Count;
    public int ReadMapInt32ValueSlot(int slot, SandboxValue key)
    {
        var map = ReadBoxedValue<MapValue>(slot);
        SandboxValueValidator.RequireType(key, map.KeyType, "map key type mismatch");
        if (!map.Values.TryGetValue(key, out var value))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.NotFound,
                "map key was not found"));
        }
        return value is I32Value item
            ? item.Value
            : throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "expected I32 value"));
    }
    public bool TryReadDouble(string name, out double value)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsF64Slot(slot))
        {
            value = RawSlotAssignmentState.IsAssigned(_assigned, slot) ? _f64Slots[slot] : 0;
            return RawSlotAssignmentState.IsAssigned(_assigned, slot);
        }
        if (TryGetBoxedValue<F64Value>(slot, out var f64))
        {
            value = f64.Value;
            return true;
        }
        value = 0;
        return false;
    }
    public double ReadDoubleSlot(int slot)
        => _layout.IsF64Slot(slot)
            ? RawSlotAssignmentState.IsAssigned(_assigned, slot) ? _f64Slots[slot] : throw UnassignedSlot()
            : TryGetBoxedValue<F64Value>(slot, out var value) ? value.Value : throw UnassignedSlot();
    public double ReadRawDoubleSlot(int slot) => _f64Slots[slot];
    public void WriteInt32(string name, int value)
    {
        var slot = _layout.GetSlot(name);
        if (!_layout.IsI32Slot(slot))
        {
            Write(name, SandboxValue.FromInt32(value));
            return;
        }
        _i32Slots[slot] = value;
        RawSlotAssignmentState.MarkAssigned(_assigned, slot);
    }
    public void WriteInt32Slot(int slot, int value)
    {
        if (!_layout.IsI32Slot(slot))
        {
            WriteBoxedValue(slot, SandboxValue.FromInt32(value));
            return;
        }
        _i32Slots[slot] = value;
        RawSlotAssignmentState.MarkAssigned(_assigned, slot);
    }

    public void WriteRawInt32Slot(int slot, int value)
    {
        _i32Slots[slot] = value;
        RawSlotAssignmentState.MarkAssigned(_assigned, slot);
    }

    public void WriteDoubleSlot(int slot, double value)
    {
        if (!_layout.IsF64Slot(slot))
        {
            WriteBoxedValue(slot, SandboxValue.FromDouble(value));
            return;
        }

        _f64Slots[slot] = value;
        RawSlotAssignmentState.MarkAssigned(_assigned, slot);
    }

    public void WriteRawDoubleSlot(int slot, double value)
    {
        _f64Slots[slot] = value;
        RawSlotAssignmentState.MarkAssigned(_assigned, slot);
    }

    public static InterpreterFrame Create(
        FunctionFrameLayout layout,
        SandboxFunction function,
        LocalFunctionArguments args)
        => InterpreterFrameBuilder.Create(layout, function, args);

    public static InterpreterFrame CreateValidatedEntrypoint(
        FunctionFrameLayout layout,
        SandboxFunction function,
        SandboxValue input)
        => InterpreterFrameBuilder.CreateValidatedEntrypoint(layout, function, input);

    private bool TryGetBoxedValue<T>(int slot, out T value)
        where T : SandboxValue
    {
        if (_layout.IsBoxedSlot(slot) && _slots[slot] is T typed)
        {
            value = typed;
            return true;
        }

        value = null!;
        return false;
    }

    private T ReadBoxedValue<T>(int slot)
        where T : SandboxValue
        => TryGetBoxedValue<T>(slot, out var value) ? value : throw UnassignedSlot();

    private void WriteBoxedValue(int slot, SandboxValue value)
    {
        if (!_layout.IsBoxedSlot(slot))
        {
            throw WrongSlotKind();
        }

        _slots[slot] = value;
    }

    private static SandboxRuntimeException Unassigned(string name)
        => new(new SandboxError(SandboxErrorCode.ValidationError, $"local '{name}' read before assignment"));

    private static SandboxRuntimeException UnassignedSlot()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "local read before assignment"));

    private static SandboxRuntimeException WrongSlotKind()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "local slot kind mismatch"));
}
