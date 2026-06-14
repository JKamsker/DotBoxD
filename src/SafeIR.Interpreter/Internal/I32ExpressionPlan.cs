namespace SafeIR.Interpreter.Internal;

using SafeIR;

internal sealed class I32ExpressionPlan
{
    private readonly ExpressionKind _kind;
    private readonly int _value;
    private readonly int _value2;
    private readonly int _value3;
    private readonly I32ExpressionPlan? _left;
    private readonly I32ExpressionPlan? _right;

    private I32ExpressionPlan(
        ExpressionKind kind,
        int value,
        I32ExpressionPlan? left = null,
        I32ExpressionPlan? right = null,
        int value2 = 0,
        int value3 = 0,
        int? fuelCost = null)
    {
        _kind = kind;
        _value = value;
        _value2 = value2;
        _value3 = value3;
        _left = left;
        _right = right;
        FuelCost = fuelCost ?? 1 + (left?.FuelCost ?? 0) + (right?.FuelCost ?? 0);
    }

    public int FuelCost { get; }

    public static bool TryCreate(
        Expression expression,
        InterpreterFrame frame,
        string assumedInt32Local,
        out I32ExpressionPlan plan)
    {
        switch (expression)
        {
            case LiteralExpression { Value: I32Value value }:
                plan = new I32ExpressionPlan(ExpressionKind.Literal, value.Value);
                return true;
            case VariableExpression variable when CanReadVariable(frame, variable.Name, assumedInt32Local):
                var slot = frame.GetSlot(variable.Name);
                plan = new I32ExpressionPlan(
                    frame.IsInt32Slot(slot) ? ExpressionKind.RawVariable : ExpressionKind.BoxedVariable,
                    slot);
                return true;
            case UnaryExpression { Operator: "-" } unary
                when TryCreate(unary.Operand, frame, assumedInt32Local, out var operand):
                plan = new I32ExpressionPlan(ExpressionKind.Negate, 0, operand);
                return true;
            case BinaryExpression binary when binary.Operator is "+" or "-" or "*" or "/" or "%":
                return TryCreateSpecialBinary(binary, frame, assumedInt32Local, out plan) ||
                       TryCreateBinary(binary, frame, assumedInt32Local, out plan);
            default:
                plan = null!;
                return false;
        }
    }

    public int Evaluate(InterpreterFrame frame)
        => _kind switch
        {
            ExpressionKind.Literal => _value,
            ExpressionKind.RawVariable => frame.ReadRawInt32Slot(_value),
            ExpressionKind.BoxedVariable => frame.ReadInt32Slot(_value),
            ExpressionKind.Negate => SandboxInt32Math.Negate(_left!.Evaluate(frame)),
            ExpressionKind.RemainderAddRawRawConst => SandboxInt32Math.Remainder(
                SandboxInt32Math.Add(frame.ReadRawInt32Slot(_value), frame.ReadRawInt32Slot(_value2)),
                _value3),
            ExpressionKind.AddRawMultiplyRawConst => SandboxInt32Math.Add(
                frame.ReadRawInt32Slot(_value),
                SandboxInt32Math.Multiply(frame.ReadRawInt32Slot(_value2), _value3)),
            ExpressionKind.Add => SandboxInt32Math.Add(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            ExpressionKind.Subtract => SandboxInt32Math.Subtract(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            ExpressionKind.Multiply => SandboxInt32Math.Multiply(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            ExpressionKind.Divide => SandboxInt32Math.Divide(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            ExpressionKind.Remainder => SandboxInt32Math.Remainder(_left!.Evaluate(frame), _right!.Evaluate(frame)),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
        };

    private static bool TryCreateSpecialBinary(
        BinaryExpression binary,
        InterpreterFrame frame,
        string assumedInt32Local,
        out I32ExpressionPlan plan)
    {
        if (binary is
            {
                Operator: "%",
                Left: BinaryExpression { Operator: "+" } add,
                Right: LiteralExpression { Value: I32Value modulo }
            } &&
            TryRawSlot(add.Left, frame, assumedInt32Local, out var leftSlot) &&
            TryRawSlot(add.Right, frame, assumedInt32Local, out var rightSlot))
        {
            plan = new I32ExpressionPlan(
                ExpressionKind.RemainderAddRawRawConst,
                leftSlot,
                value2: rightSlot,
                value3: modulo.Value,
                fuelCost: 5);
            return true;
        }

        if (binary is
            {
                Operator: "+",
                Left: VariableExpression left,
                Right: BinaryExpression
                {
                    Operator: "*",
                    Left: VariableExpression multiplied,
                    Right: LiteralExpression { Value: I32Value multiplier }
                }
            } &&
            TryRawSlot(left, frame, assumedInt32Local, out var leftSlot2) &&
            TryRawSlot(multiplied, frame, assumedInt32Local, out var multipliedSlot))
        {
            plan = new I32ExpressionPlan(
                ExpressionKind.AddRawMultiplyRawConst,
                leftSlot2,
                value2: multipliedSlot,
                value3: multiplier.Value,
                fuelCost: 5);
            return true;
        }

        plan = null!;
        return false;
    }

    private static bool TryCreateBinary(
        BinaryExpression binary,
        InterpreterFrame frame,
        string assumedInt32Local,
        out I32ExpressionPlan plan)
    {
        if (!TryCreate(binary.Left, frame, assumedInt32Local, out var left) ||
            !TryCreate(binary.Right, frame, assumedInt32Local, out var right))
        {
            plan = null!;
            return false;
        }

        plan = new I32ExpressionPlan(BinaryKind(binary.Operator), 0, left, right);
        return true;
    }

    private static bool CanReadVariable(InterpreterFrame frame, string name, string assumedInt32Local)
        => frame.CanReadInt32(name) || (name == assumedInt32Local && frame.IsInt32Local(name));

    private static bool TryRawSlot(Expression expression, InterpreterFrame frame, string assumedInt32Local, out int slot)
    {
        if (expression is VariableExpression variable)
        {
            return TryRawSlot(variable, frame, assumedInt32Local, out slot);
        }

        slot = 0;
        return false;
    }

    private static bool TryRawSlot(VariableExpression variable, InterpreterFrame frame, string assumedInt32Local, out int slot)
    {
        slot = frame.GetSlot(variable.Name);
        return frame.IsInt32Slot(slot) &&
               (frame.CanReadInt32(variable.Name) || variable.Name == assumedInt32Local);
    }

    private static ExpressionKind BinaryKind(string op)
        => op switch
        {
            "+" => ExpressionKind.Add,
            "-" => ExpressionKind.Subtract,
            "*" => ExpressionKind.Multiply,
            "/" => ExpressionKind.Divide,
            "%" => ExpressionKind.Remainder,
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
        };

    private enum ExpressionKind
    {
        Literal,
        RawVariable,
        BoxedVariable,
        Negate,
        RemainderAddRawRawConst,
        AddRawMultiplyRawConst,
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder
    }
}
