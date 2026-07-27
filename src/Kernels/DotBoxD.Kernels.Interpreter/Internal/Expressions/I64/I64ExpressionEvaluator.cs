using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal static class I64ExpressionEvaluator
{
    public static bool CanEvaluate(Expression expression, InterpreterFrame frame)
        => expression switch
        {
            LiteralExpression { Value: I64Value } => true,
            VariableExpression variable => CanReadVariable(variable, frame),
            UnaryExpression { Operator: "-" } unary => CanEvaluate(unary.Operand, frame),
            BinaryExpression binary => CanEvaluateBinary(binary, frame),
            _ => false
        };

    public static long Evaluate(
        Expression expression,
        InterpreterFrame frame,
        SandboxContext context)
    {
        context.ChargeFuel(1);
        return expression switch
        {
            LiteralExpression { Value: I64Value value } => value.Value,
            VariableExpression variable => ReadVariable(variable, frame),
            UnaryExpression { Operator: "-" } unary =>
                SandboxInt64Math.Negate(Evaluate(unary.Operand, frame, context)),
            BinaryExpression binary => EvaluateBinary(binary, frame, context),
            _ => throw Unsupported()
        };
    }

    private static bool CanReadVariable(VariableExpression variable, InterpreterFrame frame)
    {
        try
        {
            var slot = frame.GetSlot(variable.Name);
            return frame.IsI64Slot(slot) && frame.IsSlotAssigned(slot);
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    private static bool CanEvaluateBinary(BinaryExpression binary, InterpreterFrame frame)
        => binary.Operator is "+" or "-" or "*" or "/" or "%" &&
           CanEvaluate(binary.Left, frame) &&
           CanEvaluate(binary.Right, frame);

    private static long ReadVariable(VariableExpression variable, InterpreterFrame frame)
        => frame.ReadRawInt64Slot(frame.GetSlot(variable.Name));

    private static long EvaluateBinary(
        BinaryExpression binary,
        InterpreterFrame frame,
        SandboxContext context)
    {
        var left = Evaluate(binary.Left, frame, context);
        var right = Evaluate(binary.Right, frame, context);
        return binary.Operator switch
        {
            "+" => SandboxInt64Math.Add(left, right),
            "-" => SandboxInt64Math.Subtract(left, right),
            "*" => SandboxInt64Math.Multiply(left, right),
            "/" => SandboxInt64Math.Divide(left, right),
            "%" => SandboxInt64Math.Remainder(left, right),
            _ => throw Unsupported()
        };
    }

    private static SandboxRuntimeException Unsupported()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i64 expression"));
}
