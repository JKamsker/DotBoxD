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

    /// <summary>
    /// Applies ordered authored spans to a function's statement/expression tree. Extra IR nodes use the nearest
    /// authored span, which keeps generated lowering scaffolding stoppable without inventing source locations.
    /// </summary>
    public static SandboxModule ApplyFunctionSequenceSpans(
        SandboxModule module,
        IReadOnlyDictionary<string, IReadOnlyList<SourceSpan>> functionSpans)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(functionSpans);
        var functions = new SandboxFunction[module.Functions.Count];
        for (var index = 0; index < functions.Length; index++)
        {
            var function = module.Functions[index];
            if (!functionSpans.TryGetValue(function.Id, out var spans) || spans.Count == 0)
            {
                functions[index] = function;
                continue;
            }

            var cursor = new SequenceSpanCursor(spans, CountNodes(function.Body));
            functions[index] = function with { Body = RewriteStatements(function.Body, cursor.Next) };
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

    private static IReadOnlyList<Statement> RewriteStatements(
        IReadOnlyList<Statement> statements,
        Func<SourceSpan> next)
    {
        var rewritten = new Statement[statements.Count];
        for (var index = 0; index < rewritten.Length; index++)
        {
            rewritten[index] = RewriteStatement(statements[index], next);
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

    private static Statement RewriteStatement(Statement statement, Func<SourceSpan> next)
    {
        var span = next();
        return statement switch
        {
            AssignmentStatement assignment => new AssignmentStatement(
                assignment.Name, RewriteExpression(assignment.Value, next), span),
            ReturnStatement returned => new ReturnStatement(RewriteExpression(returned.Value, next), span),
            ExpressionStatement expression => new ExpressionStatement(RewriteExpression(expression.Value, next), span),
            IfStatement branch => new IfStatement(
                RewriteExpression(branch.Condition, next),
                RewriteStatements(branch.Then, next),
                RewriteStatements(branch.Else, next),
                span),
            WhileStatement loop => new WhileStatement(
                RewriteExpression(loop.Condition, next), RewriteStatements(loop.Body, next), span),
            ForRangeStatement range => new ForRangeStatement(
                range.LocalName,
                RewriteExpression(range.Start, next),
                RewriteExpression(range.End, next),
                RewriteStatements(range.Body, next),
                span),
            ContinueStatement => new ContinueStatement(span),
            BreakStatement => new BreakStatement(span),
            _ => throw new NotSupportedException($"Cannot map unsupported statement '{statement.GetType().Name}'.")
        };
    }

    private static Expression RewriteExpression(Expression expression, Func<SourceSpan> next)
    {
        var span = next();
        return expression switch
        {
            LiteralExpression literal => new LiteralExpression(literal.Value, span),
            VariableExpression variable => new VariableExpression(variable.Name, span),
            UnaryExpression unary => new UnaryExpression(unary.Operator, RewriteExpression(unary.Operand, next), span),
            BinaryExpression binary => new BinaryExpression(
                RewriteExpression(binary.Left, next), binary.Operator, RewriteExpression(binary.Right, next), span),
            CallExpression call => new CallExpression(
                call.Name,
                call.Arguments.Select(argument => RewriteExpression(argument, next)).ToArray(),
                call.GenericType,
                span),
            _ => throw new NotSupportedException($"Cannot map unsupported expression '{expression.GetType().Name}'.")
        };
    }

    private static int CountNodes(IReadOnlyList<Statement> statements)
        => statements.Sum(CountNodes);

    private static int CountNodes(Statement statement)
        => statement switch
        {
            AssignmentStatement assignment => 1 + CountNodes(assignment.Value),
            ReturnStatement returned => 1 + CountNodes(returned.Value),
            ExpressionStatement expression => 1 + CountNodes(expression.Value),
            IfStatement branch => 1 + CountNodes(branch.Condition) + CountNodes(branch.Then) + CountNodes(branch.Else),
            WhileStatement loop => 1 + CountNodes(loop.Condition) + CountNodes(loop.Body),
            ForRangeStatement range =>
                1 + CountNodes(range.Start) + CountNodes(range.End) + CountNodes(range.Body),
            ContinueStatement or BreakStatement => 1,
            _ => throw new NotSupportedException($"Cannot count unsupported statement '{statement.GetType().Name}'.")
        };

    private static int CountNodes(Expression expression)
        => expression switch
        {
            LiteralExpression or VariableExpression => 1,
            UnaryExpression unary => 1 + CountNodes(unary.Operand),
            BinaryExpression binary => 1 + CountNodes(binary.Left) + CountNodes(binary.Right),
            CallExpression call => 1 + call.Arguments.Sum(CountNodes),
            _ => throw new NotSupportedException($"Cannot count unsupported expression '{expression.GetType().Name}'.")
        };

    private sealed class SequenceSpanCursor(IReadOnlyList<SourceSpan> spans, int nodeCount)
    {
        private int _index;

        public SourceSpan Next()
        {
            var spanIndex = Math.Min((_index++ * spans.Count) / Math.Max(1, nodeCount), spans.Count - 1);
            return spans[spanIndex];
        }
    }
}
