namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class InterpreterFrame
{
    private readonly FunctionFrameLayout _layout;
    private readonly SandboxValue?[] _slots;
    private readonly int[] _i32Slots;
    private readonly bool[] _assigned;

    private InterpreterFrame(FunctionFrameLayout layout, SandboxValue?[] slots, int[] i32Slots, bool[] assigned)
    {
        _layout = layout;
        _slots = slots;
        _i32Slots = i32Slots;
        _assigned = assigned;
    }

    public string FunctionId => _layout.FunctionId;

    public int GetSlot(string name) => _layout.GetSlot(name);

    public SandboxValue Read(string name)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI32Slot(slot))
        {
            return _assigned[slot]
                ? SandboxValue.FromInt32(_i32Slots[slot])
                : throw Unassigned(name);
        }

        return _slots[slot]
            ?? throw Unassigned(name);
    }

    public void Write(string name, SandboxValue value)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI32Slot(slot))
        {
            _i32Slots[slot] = ((I32Value)value).Value;
        }
        else
        {
            _slots[slot] = value;
        }

        if (_layout.HasI32Slots)
        {
            _assigned[slot] = true;
        }
    }

    public bool CanReadInt32(string name)
    {
        var slot = _layout.GetSlot(name);
        return _layout.IsI32Slot(slot)
            ? _assigned[slot]
            : _slots[slot] is I32Value;
    }

    public bool IsInt32Local(string name) => _layout.IsI32Slot(name);

    public bool IsInt32Slot(int slot) => _layout.IsI32Slot(slot);

    public int ReadInt32(string name)
    {
        var slot = _layout.GetSlot(name);
        if (_layout.IsI32Slot(slot))
        {
            return _assigned[slot] ? _i32Slots[slot] : throw Unassigned(name);
        }

        return _slots[slot] is I32Value value ? value.Value : throw Unassigned(name);
    }

    public int ReadInt32Slot(int slot)
        => _layout.IsI32Slot(slot)
            ? _assigned[slot] ? _i32Slots[slot] : throw UnassignedSlot()
            : _slots[slot] is I32Value value ? value.Value : throw UnassignedSlot();

    public int ReadRawInt32Slot(int slot) => _i32Slots[slot];

    public void WriteInt32(string name, int value)
    {
        var slot = _layout.GetSlot(name);
        if (!_layout.IsI32Slot(slot))
        {
            Write(name, SandboxValue.FromInt32(value));
            return;
        }

        _i32Slots[slot] = value;
        _assigned[slot] = true;
    }

    public void WriteInt32Slot(int slot, int value)
    {
        if (!_layout.IsI32Slot(slot))
        {
            _slots[slot] = SandboxValue.FromInt32(value);
            return;
        }

        _i32Slots[slot] = value;
        _assigned[slot] = true;
    }

    public void WriteRawInt32Slot(int slot, int value)
    {
        _i32Slots[slot] = value;
        _assigned[slot] = true;
    }

    public static InterpreterFrame Create(
        FunctionFrameLayout layout,
        SandboxFunction function,
        IReadOnlyList<SandboxValue> args)
    {
        var slots = layout.SlotCount == 0
            ? System.Array.Empty<SandboxValue?>()
            : new SandboxValue?[layout.SlotCount];
        var i32Slots = layout.HasI32Slots ? new int[layout.SlotCount] : System.Array.Empty<int>();
        var assigned = layout.HasI32Slots ? new bool[layout.SlotCount] : System.Array.Empty<bool>();

        // Parameters occupy the leading slots in declaration order (see
        // FunctionFrameLayout.Build), so positional arguments map directly.
        for (var i = 0; i < function.Parameters.Count; i++) {
            if (layout.IsI32Slot(i))
            {
                i32Slots[i] = ((I32Value)args[i]).Value;
            }
            else
            {
                slots[i] = args[i];
            }

            if (layout.HasI32Slots)
            {
                assigned[i] = true;
            }
        }

        return new InterpreterFrame(layout, slots, i32Slots, assigned);
    }

    private static SandboxRuntimeException Unassigned(string name)
        => new(new SandboxError(SandboxErrorCode.ValidationError, $"local '{name}' read before assignment"));

    private static SandboxRuntimeException UnassignedSlot()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "local read before assignment"));
}
