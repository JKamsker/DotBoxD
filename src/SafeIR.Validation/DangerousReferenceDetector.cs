namespace SafeIR.Validation;

using SafeIR;

internal static class DangerousReferenceDetector
{
    private static readonly string[] ForbiddenFragments = [
        "System.", "Microsoft.", "Assembly.", "Type.", "Reflection.", "Process.",
        "Environment.", "Thread.", "Task.", "DllImport", "IServiceProvider"
    ];

    private static readonly string[] ForbiddenIlFragments = [
        "IL_", "ldtoken", "ldftn", "ldvirtftn", "calli"
    ];

    private static readonly string[] MetadataTokenPrefixes = [
        "0x02", "0x04", "0x06", "0x0a", "0x1b", "0x23", "0x70"
    ];

    public static bool IsDangerousReference(string value)
        => ForbiddenFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
           ForbiddenIlFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
           ContainsMetadataToken(value);

    public static void Scan(Statement statement, List<SandboxDiagnostic> diagnostics)
    {
        switch (statement)
        {
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
        switch (expression)
        {
            case LiteralExpression literal:
                CheckLiteral(literal, diagnostics);
                break;
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
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-IR-ID",
                "IR identifiers and call names must be non-empty and must not contain control characters",
                Span: span));
            return;
        }

        if (IsDangerousReference(value))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", $"forbidden CLR reference '{value}'", Span: span));
        }
    }

    private static void CheckLiteral(LiteralExpression literal, List<SandboxDiagnostic> diagnostics)
    {
        var text = literal.Value switch
        {
            StringValue value => value.Value,
            SandboxPathValue value => value.Value.RelativePath,
            SandboxUriValue value => value.Value.Value,
            _ => null
        };
        if (text is not null && IsDangerousReference(text))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", "forbidden CLR reference in literal", Span: literal.Span));
        }
    }

    private static bool ContainsMetadataToken(string value)
    {
        for (var index = 0; index <= value.Length - 10; index++)
        {
            var candidate = value.Substring(index, 4);
            if (!MetadataTokenPrefixes.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Enumerable.Range(index + 4, 6).All(i => IsHex(value[i])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHex(char value)
        => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
