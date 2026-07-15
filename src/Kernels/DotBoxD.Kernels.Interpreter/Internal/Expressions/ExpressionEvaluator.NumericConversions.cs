using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal sealed partial class ExpressionEvaluator
{
    private ValueTask<SandboxValue> EvaluateNumericConversion(
        string name,
        Expression operandExpression,
        InterpreterFrame frame)
    {
        var operand = EvaluateAsync(operandExpression, frame);
        return operand.IsCompletedSuccessfully
            ? new ValueTask<SandboxValue>(ConvertNumeric(name, operand.Result))
            : AwaitNumericConversion(name, operand);
    }

    private static async ValueTask<SandboxValue> AwaitNumericConversion(
        string name,
        ValueTask<SandboxValue> operand)
        => ConvertNumeric(name, await operand.ConfigureAwait(false));

    private static SandboxValue NumericToInt64(SandboxValue value)
        => value switch
        {
            I32Value number => SandboxValue.FromInt64(number.Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "expected I32 value"))
        };

    private static SandboxValue NumericToDouble(SandboxValue value)
        => value switch
        {
            I32Value number => SandboxValue.FromDouble(number.Value),
            I64Value number => SandboxValue.FromDouble(number.Value),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "expected I32 or I64 value"))
        };

    private static SandboxValue ConvertNumeric(string name, SandboxValue value)
        => name switch
        {
            "numeric.toI64" => NumericToInt64(value),
            "numeric.toF64" => NumericToDouble(value),
            _ => throw new InvalidOperationException("unsupported numeric conversion")
        };

    private static bool IsNumericConversion(string name)
        => name is "numeric.toI64" or "numeric.toF64";
}
