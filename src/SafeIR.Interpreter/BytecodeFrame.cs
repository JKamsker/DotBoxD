namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class BytecodeFrame
{
    private readonly Stack<SandboxValue> _stack = [];

    private BytecodeFrame(Dictionary<string, SandboxValue> locals) => Locals = locals;

    public Dictionary<string, SandboxValue> Locals { get; }

    public int InstructionPointer { get; set; }

    public static BytecodeFrame Create(BytecodeFunction function, IReadOnlyList<SandboxValue> args)
    {
        var locals = new Dictionary<string, SandboxValue>(StringComparer.Ordinal);
        for (var i = 0; i < function.Parameters.Count; i++) {
            locals[function.Parameters[i].Name] = args[i];
        }

        return new BytecodeFrame(locals);
    }

    public void Push(SandboxValue value) => _stack.Push(value);

    public SandboxValue Pop()
        => _stack.Count > 0
            ? _stack.Pop()
            : throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "bytecode stack underflow"));

    public IReadOnlyList<SandboxValue> PopArguments(int count)
    {
        var values = new SandboxValue[count];
        for (var i = count - 1; i >= 0; i--) {
            values[i] = Pop();
        }

        return values;
    }
}
