using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Expressions;

// Recognition for fused i32 plan kinds whose operands may be supplied through an inline-call substitution.
internal sealed partial class I32ExpressionPlan
{
    private static bool TryCreateRemainderAddRawConstConst(
        BinaryExpression binary,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32ExpressionSubstitution substitution,
        out I32ExpressionPlan plan)
    {
        plan = null!;
        if (binary is not
            {
                Operator: "%",
                Left: BinaryExpression { Operator: "+" } add,
                Right: LiteralExpression { Value: I32Value modulo }
            })
        {
            return false;
        }

        // Accept either operand order: (raw + const) or (const + raw).
        if ((TryResolveRawSlot(add.Left, frame, assumedInt32Local, substitution, out var slot) &&
             TryConstI32(add.Right, out var addend)) ||
            (TryResolveRawSlot(add.Right, frame, assumedInt32Local, substitution, out slot) &&
             TryConstI32(add.Left, out addend)))
        {
            plan = new I32ExpressionPlan(
                ExpressionKind.RemainderAddRawConstConst,
                slot,
                value2: addend,
                value3: modulo.Value,
                fuelCost: 5);
            return true;
        }

        return false;
    }

    private static bool TryResolveRawSlot(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        I32ExpressionSubstitution substitution,
        out int slot)
    {
        slot = 0;
        if (expression is not VariableExpression variable)
        {
            return false;
        }

        if (substitution.TryGetValue(variable.Name, out var replacement))
        {
            if (replacement._kind == ExpressionKind.RawVariable)
            {
                slot = replacement._value;
                return true;
            }

            return false;
        }

        return TryRawSlot(variable, frame, assumedInt32Local, out slot);
    }

    private static bool TryConstI32(Expression expression, out int value)
    {
        if (expression is LiteralExpression { Value: I32Value literal })
        {
            value = literal.Value;
            return true;
        }

        value = 0;
        return false;
    }
}
