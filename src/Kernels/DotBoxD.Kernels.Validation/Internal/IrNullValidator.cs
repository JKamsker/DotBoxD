using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

internal static class IrNullValidator
{
    public static void Scan(Statement? statement, List<SandboxDiagnostic> diagnostics)
        => ScanStatement(statement, "function body entry", diagnostics, new SourceSpan(0, 0));

    private static void ScanStatement(
        Statement? statement,
        string location,
        List<SandboxDiagnostic> diagnostics,
        SourceSpan fallbackSpan)
    {
        if (statement is null)
        {
            AddNull(location, diagnostics, fallbackSpan);
            return;
        }

        switch (statement)
        {
            case AssignmentStatement assignment:
                ScanExpression(assignment.Value, "assignment value", diagnostics, assignment.Span);
                break;
            case ReturnStatement ret:
                ScanExpression(ret.Value, "return value", diagnostics, ret.Span);
                break;
            case ExpressionStatement expr:
                ScanExpression(expr.Value, "expression statement value", diagnostics, expr.Span);
                break;
            case IfStatement branch:
                ScanExpression(branch.Condition, "if condition", diagnostics, branch.Span);
                ScanBlock(branch.Then, "if then block", diagnostics, branch.Span);
                ScanBlock(branch.Else, "if else block", diagnostics, branch.Span);
                break;
            case WhileStatement loop:
                ScanExpression(loop.Condition, "while condition", diagnostics, loop.Span);
                ScanBlock(loop.Body, "while body block", diagnostics, loop.Span);
                break;
            case ForRangeStatement range:
                ScanExpression(range.Start, "for range start", diagnostics, range.Span);
                ScanExpression(range.End, "for range end", diagnostics, range.Span);
                ScanBlock(range.Body, "for range body block", diagnostics, range.Span);
                break;
        }
    }

    private static void ScanBlock(
        IReadOnlyList<Statement> block,
        string location,
        List<SandboxDiagnostic> diagnostics,
        SourceSpan fallbackSpan)
    {
        for (var i = 0; i < block.Count; i++)
        {
            ScanStatement(block[i], $"{location} entry", diagnostics, fallbackSpan);
        }
    }

    private static void ScanExpression(
        Expression? expression,
        string location,
        List<SandboxDiagnostic> diagnostics,
        SourceSpan fallbackSpan)
    {
        if (expression is null)
        {
            AddNull(location, diagnostics, fallbackSpan);
            return;
        }

        switch (expression)
        {
            case UnaryExpression unary:
                ScanExpression(unary.Operand, "unary operand", diagnostics, unary.Span);
                break;
            case BinaryExpression binary:
                ScanExpression(binary.Left, "binary left operand", diagnostics, binary.Span);
                ScanExpression(binary.Right, "binary right operand", diagnostics, binary.Span);
                break;
            case CallExpression call:
                for (var i = 0; i < call.Arguments.Count; i++)
                {
                    ScanExpression(call.Arguments[i], "call argument", diagnostics, call.Span);
                }

                break;
        }
    }

    private static void AddNull(string location, List<SandboxDiagnostic> diagnostics, SourceSpan span)
        => diagnostics.Add(new SandboxDiagnostic("E-IR-NULL", $"{location} must not be null", Span: span));
}
