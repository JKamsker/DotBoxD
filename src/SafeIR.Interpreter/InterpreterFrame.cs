namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class InterpreterFrame
{
    private readonly FunctionFrameLayout _layout;
    private readonly SandboxValue?[] _slots;

    private InterpreterFrame(FunctionFrameLayout layout, SandboxValue?[] slots)
    {
        _layout = layout;
        _slots = slots;
    }

    public string FunctionId => _layout.FunctionId;

    public SandboxValue Read(string name)
    {
        var slot = _layout.GetSlot(name);
        return _slots[slot]
            ?? throw new SandboxRuntimeException(
                new SandboxError(SandboxErrorCode.ValidationError, $"local '{name}' read before assignment"));
    }

    public void Write(string name, SandboxValue value)
        => _slots[_layout.GetSlot(name)] = value;

    public static InterpreterFrame Create(
        FunctionFrameLayout layout,
        SandboxFunction function,
        IReadOnlyList<SandboxValue> args)
    {
        var slots = layout.SlotCount == 0
            ? System.Array.Empty<SandboxValue?>()
            : new SandboxValue?[layout.SlotCount];

        // Parameters occupy the leading slots in declaration order (see
        // FunctionFrameLayout.Build), so positional arguments map directly.
        for (var i = 0; i < function.Parameters.Count; i++) {
            slots[i] = args[i];
        }

        return new InterpreterFrame(layout, slots);
    }
}
