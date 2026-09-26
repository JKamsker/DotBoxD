namespace DotBoxD.Plugins.Inspection;

internal static class LoweredOperationCollector
{
    public static LoweredOperation[] Collect(SandboxModule module)
    {
        var operations = new List<LoweredOperation>();
        foreach (var function in module.Functions)
        {
            var pending = new Stack<object>(function.Body.Reverse());
            while (pending.TryPop(out var node))
            {
                if (operations.Count >= 100_000)
                {
                    throw new InvalidDataException("Inspection exceeds the operation limit.");
                }
                switch (node)
                {
                    case Statement statement:
                        operations.Add(new LoweredOperation(function.Id, statement.GetType().Name, statement.Span));
                        PushStatementChildren(pending, statement);
                        break;
                    case Expression expression:
                        operations.Add(new LoweredOperation(function.Id, ExpressionName(expression), expression.Span));
                        PushExpressionChildren(pending, expression);
                        break;
                }
            }
        }
        return operations.ToArray();
    }

    private static string ExpressionName(Expression expression) => expression switch
    {
        CallExpression call => $"call {call.Name}",
        BinaryExpression binary => $"binary {binary.Operator}",
        UnaryExpression unary => $"unary {unary.Operator}",
        _ => expression.GetType().Name
    };

    private static void PushStatementChildren(Stack<object> pending, Statement statement)
    {
        switch (statement)
        {
            case AssignmentStatement assignment:
                pending.Push(assignment.Value);
                break;
            case ReturnStatement returned:
                pending.Push(returned.Value);
                break;
            case ExpressionStatement expression:
                pending.Push(expression.Value);
                break;
            case IfStatement conditional:
                PushAll(pending, conditional.Else);
                PushAll(pending, conditional.Then);
                pending.Push(conditional.Condition);
                break;
            case WhileStatement loop:
                PushAll(pending, loop.Body);
                pending.Push(loop.Condition);
                break;
            case ForRangeStatement range:
                PushAll(pending, range.Body);
                pending.Push(range.End);
                pending.Push(range.Start);
                break;
        }
    }

    private static void PushExpressionChildren(Stack<object> pending, Expression expression)
    {
        switch (expression)
        {
            case UnaryExpression unary:
                pending.Push(unary.Operand);
                break;
            case BinaryExpression binary:
                pending.Push(binary.Right);
                pending.Push(binary.Left);
                break;
            case CallExpression call:
                PushAll(pending, call.Arguments);
                break;
        }
    }

    private static void PushAll<T>(Stack<object> pending, IReadOnlyList<T> nodes) where T : class
    {
        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            pending.Push(nodes[index]);
        }
    }
}
