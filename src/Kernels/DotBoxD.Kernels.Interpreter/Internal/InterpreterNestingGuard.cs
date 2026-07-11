using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal static class InterpreterNestingGuard
{
    internal const int MaxDepth = 128;
    private static readonly ConditionalWeakTable<ExecutionPlan, ValidatedPlan> ValidatedPlans = new();

    public static void ThrowIfExceeded(ExecutionPlan plan)
        => _ = ValidatedPlans.GetValue(plan, Validate);

    private static ValidatedPlan Validate(ExecutionPlan plan)
    {
        var pending = new Stack<(object Node, int Depth)>();
        foreach (var function in plan.Module.Functions)
        {
            PushStatements(pending, function.Body, depth: 1);
        }

        while (pending.TryPop(out var current))
        {
            if (current.Depth > MaxDepth)
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.QuotaExceeded,
                    $"interpreter nesting exceeds the maximum depth of {MaxDepth}"));
            }

            PushChildren(pending, current.Node, current.Depth + 1);
        }

        return ValidatedPlan.Instance;
    }

    private static void PushChildren(Stack<(object Node, int Depth)> pending, object node, int depth)
    {
        switch (node)
        {
            case AssignmentStatement assignment:
                pending.Push((assignment.Value, depth));
                break;
            case ReturnStatement result:
                pending.Push((result.Value, depth));
                break;
            case ExpressionStatement expression:
                pending.Push((expression.Value, depth));
                break;
            case IfStatement conditional:
                pending.Push((conditional.Condition, depth));
                PushStatements(pending, conditional.Then, depth);
                PushStatements(pending, conditional.Else, depth);
                break;
            case WhileStatement loop:
                pending.Push((loop.Condition, depth));
                PushStatements(pending, loop.Body, depth);
                break;
            case ForRangeStatement loop:
                pending.Push((loop.Start, depth));
                pending.Push((loop.End, depth));
                PushStatements(pending, loop.Body, depth);
                break;
            case UnaryExpression unary:
                pending.Push((unary.Operand, depth));
                break;
            case BinaryExpression binary:
                pending.Push((binary.Left, depth));
                pending.Push((binary.Right, depth));
                break;
            case CallExpression call:
                foreach (var argument in call.Arguments)
                {
                    pending.Push((argument, depth));
                }
                break;
        }
    }

    private static void PushStatements(
        Stack<(object Node, int Depth)> pending,
        IReadOnlyList<Statement> statements,
        int depth)
    {
        foreach (var statement in statements)
        {
            pending.Push((statement, depth));
        }
    }

    private sealed class ValidatedPlan
    {
        public static ValidatedPlan Instance { get; } = new();
    }
}
