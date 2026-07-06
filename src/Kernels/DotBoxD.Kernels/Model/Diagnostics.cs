using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Model;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record SandboxDiagnostic(
    string Code,
    string Message,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error,
    SourceSpan? Span = null)
{
    private string _code = Code ?? throw new ArgumentNullException(nameof(Code));
    private string _message = Message ?? throw new ArgumentNullException(nameof(Message));
    private DiagnosticSeverity _severity = ValidateSeverity(Severity);

    public string Code { get => _code; init => _code = value ?? throw new ArgumentNullException(nameof(value)); }

    public string Message { get => _message; init => _message = value ?? throw new ArgumentNullException(nameof(value)); }

    public DiagnosticSeverity Severity { get => _severity; init => _severity = ValidateSeverity(value); }

    private static DiagnosticSeverity ValidateSeverity(DiagnosticSeverity severity)
        => Enum.IsDefined(severity)
            ? severity
            : throw new ArgumentOutOfRangeException(nameof(Severity), severity, "Unsupported diagnostic severity.");
}

public sealed record SourceSpan(int Line, int Column)
{
    private int _line = ValidateCoordinate(Line, nameof(Line));
    private int _column = ValidateCoordinate(Column, nameof(Column));

    public int Line { get => _line; init => _line = ValidateCoordinate(value, nameof(value)); }

    public int Column { get => _column; init => _column = ValidateCoordinate(value, nameof(value)); }

    private static int ValidateCoordinate(int value, string paramName)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(paramName, value, "Source coordinates must be non-negative.");
}

public sealed class SandboxValidationException : Exception
{
    public SandboxValidationException(IReadOnlyList<SandboxDiagnostic> diagnostics)
        : this(CreateState(diagnostics))
    {
    }

    private SandboxValidationException((IReadOnlyList<SandboxDiagnostic> Diagnostics, string Message) state)
        : base(state.Message)
    {
        Diagnostics = state.Diagnostics;
    }

    public IReadOnlyList<SandboxDiagnostic> Diagnostics { get; }

    private static (IReadOnlyList<SandboxDiagnostic> Diagnostics, string Message) CreateState(
        IReadOnlyList<SandboxDiagnostic> diagnostics)
    {
        var copy = CopyDiagnostics(diagnostics);
        return (copy, string.Join(Environment.NewLine, copy.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static IReadOnlyList<SandboxDiagnostic> CopyDiagnostics(IReadOnlyList<SandboxDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var copy = ModelCopy.List(diagnostics);
        return copy.Any(static diagnostic => diagnostic is null)
            ? throw new ArgumentException("Diagnostics cannot contain null entries.", nameof(diagnostics))
            : copy;
    }
}

public sealed class SandboxRuntimeException : Exception
{
    public SandboxRuntimeException(SandboxError error)
        : base(error.SafeMessage)
    {
        Error = error;
    }

    public SandboxError Error { get; }
}
