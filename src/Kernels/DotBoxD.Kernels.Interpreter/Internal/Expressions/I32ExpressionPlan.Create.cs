using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Expressions;

internal sealed partial class I32ExpressionPlan
{
    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        out I32ExpressionPlan plan)
        => TryCreate(expression, frame, assumedInt32Local, calls: null, substitution: default, out plan);

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator calls,
        out I32ExpressionPlan plan)
        => TryCreate(expression, frame, assumedInt32Local, calls, substitution: default, out plan);

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator? calls,
        I32ExpressionSubstitution substitution,
        out I32ExpressionPlan plan)
    {
        if (TryCreateLiteral(expression, out plan))
        {
            return true;
        }

        if (TryCreateVariable(expression, frame, assumedInt32Local, substitution, out plan))
        {
            return true;
        }

        if (TryCreateUnary(expression, frame, assumedInt32Local, calls, substitution, out plan))
        {
            return true;
        }

        if (TryCreateBinaryExpression(expression, frame, assumedInt32Local, calls, substitution, out plan))
        {
            return true;
        }

        return TryCreateCall(expression, frame, assumedInt32Local, calls, out plan);
    }

    private static bool TryCreateLiteral(Expression expression, out I32ExpressionPlan plan)
    {
        if (expression is LiteralExpression { Value: I32Value value })
        {
            plan = new I32ExpressionPlan(ExpressionKind.Literal, value.Value);
            return true;
        }

        plan = null!;
        return false;
    }

    private static bool TryCreateVariable(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32ExpressionSubstitution substitution,
        out I32ExpressionPlan plan)
    {
        if (expression is not VariableExpression variable)
        {
            plan = null!;
            return false;
        }

        if (substitution.TryGetValue(variable.Name, out var replacement))
        {
            plan = replacement;
            return true;
        }

        if (!CanReadVariable(frame, variable.Name, assumedInt32Local))
        {
            plan = null!;
            return false;
        }

        var slot = frame.GetSlot(variable.Name);
        plan = new I32ExpressionPlan(
            frame.IsInt32Slot(slot) ? ExpressionKind.RawVariable : ExpressionKind.BoxedVariable,
            slot);
        return true;
    }

    private static bool TryCreateUnary(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator? calls,
        I32ExpressionSubstitution substitution,
        out I32ExpressionPlan plan)
    {
        if (expression is UnaryExpression { Operator: "-" } unary &&
            TryCreate(unary.Operand, frame, assumedInt32Local, calls, substitution, out var operand))
        {
            plan = new I32ExpressionPlan(ExpressionKind.Negate, 0, operand);
            return true;
        }

        plan = null!;
        return false;
    }

    private static bool TryCreateBinaryExpression(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator? calls,
        I32ExpressionSubstitution substitution,
        out I32ExpressionPlan plan)
    {
        if (expression is not BinaryExpression binary)
        {
            plan = null!;
            return false;
        }

        if (!IsI32BinaryOperator(binary.Operator))
        {
            plan = null!;
            return false;
        }

        if (TryCreateSpecialBinary(binary, frame, assumedInt32Local, substitution, out plan))
        {
            return true;
        }

        return TryCreateBinary(binary, frame, assumedInt32Local, calls, substitution, out plan);
    }

    private static bool TryCreateCall(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32CallEvaluator? calls,
        out I32ExpressionPlan plan)
    {
        if (expression is CallExpression call &&
            calls?.TryCreateInt32CallPlan(call, frame, assumedInt32Local, out var callPlan) == true)
        {
            plan = callPlan;
            return true;
        }

        plan = null!;
        return false;
    }

    private static bool IsI32BinaryOperator(string op)
        => op is "+" or "-" or "*" or "/" or "%";
}
