namespace SafeIR.Interpreter;

using SafeIR;

internal sealed class InterpreterFrame
{
    private InterpreterFrame(string functionId, Dictionary<string, SandboxValue> locals)
    {
        FunctionId = functionId;
        Locals = locals;
    }

    public string FunctionId { get; }

    public Dictionary<string, SandboxValue> Locals { get; }

    public static InterpreterFrame Create(SandboxFunction function, IReadOnlyList<SandboxValue> args)
    {
        var locals = new Dictionary<string, SandboxValue>(StringComparer.Ordinal);
        for (var i = 0; i < function.Parameters.Count; i++) {
            locals[function.Parameters[i].Name] = args[i];
        }

        return new InterpreterFrame(function.Id, locals);
    }
}
