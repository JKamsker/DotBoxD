namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class InterpreterFrame
{
    private InterpreterFrame(Dictionary<string, SandboxValue> locals) => Locals = locals;

    public Dictionary<string, SandboxValue> Locals { get; }

    public static InterpreterFrame Create(SandboxFunction function, IReadOnlyList<SandboxValue> args)
    {
        var locals = new Dictionary<string, SandboxValue>(StringComparer.Ordinal);
        for (var i = 0; i < function.Parameters.Count; i++) {
            locals[function.Parameters[i].Name] = args[i];
        }

        return new InterpreterFrame(locals);
    }
}
