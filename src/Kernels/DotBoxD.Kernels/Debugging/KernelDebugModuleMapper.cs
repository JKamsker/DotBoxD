using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Debugging;

/// <summary>Public helper for applying function-level source mappings to handwritten or generated IR.</summary>
public static class KernelDebugModuleMapper
{
    public static SandboxModule ApplyFunctionSpans(
        SandboxModule module,
        IReadOnlyDictionary<string, SourceSpan> functionSpans)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(functionSpans);
        var functions = new SandboxFunction[module.Functions.Count];
        for (var index = 0; index < functions.Length; index++)
        {
            var function = module.Functions[index];
            functions[index] = functionSpans.TryGetValue(function.Id, out var span)
                ? function with { Body = RewriteStatements(function.Body, span) }
                : function;
        }

        return module with { Functions = functions };
    }

    private static IReadOnlyList<Statement> RewriteStatements(IReadOnlyList<Statement> statements, SourceSpan span)
    {
        var rewritten = new Statement[statements.Count];
        for (var index = 0; index < rewritten.Length; index++)
        {
            rewritten[index] = RewriteStatement(statements[index], span);
        }

        return rewritten;
    }

    private static Statement RewriteStatement(Statement statement, SourceSpan span)
        => statement switch
        {
            AssignmentStatement assignment => new AssignmentStatement(
                assignment.Name,
                RewriteExpression(assignment.Value, span),
                span),
            ReturnStatement returned => new ReturnStatement(RewriteExpression(returned.Value, span), span),
            ExpressionStatement expression => new ExpressionStatement(RewriteExpression(expression.Value, span), span),
            IfStatement branch => new IfStatement(
                RewriteExpression(branch.Condition, span),
                RewriteStatements(branch.Then, span),
                RewriteStatements(branch.Else, span),
                span),
            WhileStatement loop => new WhileStatement(
                RewriteExpression(loop.Condition, span),
                RewriteStatements(loop.Body, span),
                span),
            ForRangeStatement range => new ForRangeStatement(
                range.LocalName,
                RewriteExpression(range.Start, span),
                RewriteExpression(range.End, span),
                RewriteStatements(range.Body, span),
                span),
            ContinueStatement => new ContinueStatement(span),
            BreakStatement => new BreakStatement(span),
            _ => throw new NotSupportedException($"Cannot map unsupported statement '{statement.GetType().Name}'.")
        };

    private static Expression RewriteExpression(Expression expression, SourceSpan span)
        => expression switch
        {
            LiteralExpression literal => new LiteralExpression(literal.Value, span),
            VariableExpression variable => new VariableExpression(variable.Name, span),
            UnaryExpression unary => new UnaryExpression(
                unary.Operator,
                RewriteExpression(unary.Operand, span),
                span),
            BinaryExpression binary => new BinaryExpression(
                RewriteExpression(binary.Left, span),
                binary.Operator,
                RewriteExpression(binary.Right, span),
                span),
            CallExpression call => new CallExpression(
                call.Name,
                call.Arguments.Select(argument => RewriteExpression(argument, span)).ToArray(),
                call.GenericType,
                span),
            _ => throw new NotSupportedException($"Cannot map unsupported expression '{expression.GetType().Name}'.")
        };
}
