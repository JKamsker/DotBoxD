namespace SafeIR.Validation;

using SafeIR;

internal sealed class FunctionScope
{
    private readonly Dictionary<string, SandboxType> _locals;

    private FunctionScope(Dictionary<string, SandboxType> locals) => _locals = locals;

    public static FunctionScope FromParameters(IEnumerable<Parameter> parameters)
        => new(parameters.ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal));

    public FunctionScope Clone() => new(new Dictionary<string, SandboxType>(_locals, StringComparer.Ordinal));

    public SandboxType Get(string name, List<SandboxDiagnostic> diagnostics, SourceSpan span)
    {
        if (_locals.TryGetValue(name, out var type)) {
            return type;
        }

        diagnostics.Add(new SandboxDiagnostic("E-LOCAL-UNKNOWN", $"unknown local '{name}'", Span: span));
        return SandboxType.Unit;
    }

    public void Set(string name, SandboxType type, List<SandboxDiagnostic> diagnostics, SourceSpan span)
    {
        if (_locals.TryGetValue(name, out var existing) && existing != type) {
            diagnostics.Add(new SandboxDiagnostic("E-LOCAL-TYPE", $"local '{name}' changes type from {existing} to {type}", Span: span));
            return;
        }

        _locals[name] = type;
    }
}
