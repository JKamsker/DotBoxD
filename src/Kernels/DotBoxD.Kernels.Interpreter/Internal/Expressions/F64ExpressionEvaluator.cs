using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

internal static class F64ExpressionEvaluator
{
    public static bool CanEvaluate(Expression expression, InterpreterFrame frame)
        => expression switch
        {
            LiteralExpression { Value: F64Value } => true,
            VariableExpression variable => CanReadVariable(variable, frame),
            UnaryExpression { Operator: "-" } unary => CanEvaluate(unary.Operand, frame),
            BinaryExpression binary => CanEvaluateBinary(binary, frame),
            _ => false
        };

    public static double Evaluate(
        Expression expression,
        InterpreterFrame frame,
        SandboxContext context)
    {
        context.ChargeFuel(1);
        return expression switch
        {
            LiteralExpression { Value: F64Value value } => value.Value,
            VariableExpression variable => ReadVariable(variable, frame),
            UnaryExpression { Operator: "-" } unary =>
                SandboxFloat64Math.Negate(Evaluate(unary.Operand, frame, context)),
            BinaryExpression binary => EvaluateBinary(binary, frame, context),
            _ => throw Unsupported()
        };
    }

    private static bool CanReadVariable(VariableExpression variable, InterpreterFrame frame)
    {
        try
        {
            var slot = frame.GetSlot(variable.Name);
            return frame.IsF64Slot(slot) && frame.IsSlotAssigned(slot);
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

    private static double ReadVariable(VariableExpression variable, InterpreterFrame frame)
        => frame.ReadRawDoubleSlot(frame.GetSlot(variable.Name));

    private static double EvaluateBinary(
        BinaryExpression binary,
        InterpreterFrame frame,
        SandboxContext context)
    {
        var left = Evaluate(binary.Left, frame, context);
        var right = Evaluate(binary.Right, frame, context);
        return binary.Operator switch
        {
            "+" => SandboxFloat64Math.Add(left, right),
            "-" => SandboxFloat64Math.Subtract(left, right),
            "*" => SandboxFloat64Math.Multiply(left, right),
            "/" => SandboxFloat64Math.Divide(left, right),
            "%" => SandboxFloat64Math.Remainder(left, right),
            _ => throw Unsupported()
        };
    }

    private static SandboxRuntimeException Unsupported()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "unsupported f64 expression"));
}
