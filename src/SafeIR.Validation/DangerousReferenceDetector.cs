namespace SafeIR.Validation;

using SafeIR;

internal static class DangerousReferenceDetector
{
    private static readonly string[] ForbiddenFragments = [
        "System.", "Microsoft.", "Assembly.", "Type.", "Reflection.", "Process.",
        "Environment.", "Thread.", "Task.", "DllImport", "IServiceProvider"
    ];

    public static bool IsDangerousReference(string value)
        => ForbiddenFragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));

    public static void Scan(Statement statement, List<SandboxDiagnostic> diagnostics)
    {
        switch (statement) {
            case AssignmentStatement assignment:
                Check(assignment.Name, diagnostics, assignment.Span);
                Scan(assignment.Value, diagnostics);
                break;
            case ReturnStatement ret:
                Scan(ret.Value, diagnostics);
                break;
            case ExpressionStatement expr:
                Scan(expr.Value, diagnostics);
                break;
            case IfStatement branch:
                Scan(branch.Condition, diagnostics);
                branch.Then.ToList().ForEach(s => Scan(s, diagnostics));
                branch.Else.ToList().ForEach(s => Scan(s, diagnostics));
                break;
            case WhileStatement loop:
                Scan(loop.Condition, diagnostics);
                loop.Body.ToList().ForEach(s => Scan(s, diagnostics));
                break;
            case ForRangeStatement range:
                Check(range.LocalName, diagnostics, range.Span);
                Scan(range.Start, diagnostics);
                Scan(range.End, diagnostics);
                range.Body.ToList().ForEach(s => Scan(s, diagnostics));
                break;
        }
    }

    private static void Scan(Expression expression, List<SandboxDiagnostic> diagnostics)
    {
        switch (expression) {
            case VariableExpression variable:
                Check(variable.Name, diagnostics, variable.Span);
                break;
            case UnaryExpression unary:
                Scan(unary.Operand, diagnostics);
                break;
            case BinaryExpression binary:
                Scan(binary.Left, diagnostics);
                Scan(binary.Right, diagnostics);
                break;
            case CallExpression call:
                Check(call.Name, diagnostics, call.Span);
                call.Arguments.ToList().ForEach(a => Scan(a, diagnostics));
                break;
        }
    }

    private static void Check(string value, List<SandboxDiagnostic> diagnostics, SourceSpan span)
    {
        if (IsDangerousReference(value)) {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", $"forbidden CLR reference '{value}'", Span: span));
        }
    }
}
