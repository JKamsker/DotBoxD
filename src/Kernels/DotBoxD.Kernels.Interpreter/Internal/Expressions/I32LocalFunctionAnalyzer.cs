using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal static class I32LocalFunctionAnalyzer
{
    public static bool TryGetConstantReturn(
        SandboxFunction function,
        out Expression expression)
    {
        if (function.Body.Count == 1 &&
            function.Body[0] is ReturnStatement ret &&
            I32ExpressionEvaluator.CanEvaluate(ret.Value, frame: null))
        {
            expression = ret.Value;
            return true;
        }

        expression = null!;
        return false;
    }

    public static bool TryGetInlineableReturn(
        SandboxFunction function,
        CallExpression call,
        out Expression expression)
    {
        if (call.Arguments.Count == 1 &&
            IsSimpleInlineArgument(call.Arguments[0]) &&
            function.Parameters.Count == 1 &&
            function.Parameters[0].Type == SandboxType.I32 &&
            function.ReturnType == SandboxType.I32 &&
            function.Body.Count == 1 &&
            function.Body[0] is ReturnStatement ret &&
            !ContainsCall(ret.Value) &&
            CountVariableUses(ret.Value, function.Parameters[0].Name) == 1)
        {
            expression = ret.Value;
            return true;
        }

        expression = null!;
        return false;
    }

    private static int CountVariableUses(Expression expression, string name)
        => expression switch
        {
            VariableExpression variable => string.Equals(variable.Name, name, StringComparison.Ordinal) ? 1 : 0,
            UnaryExpression unary => CountVariableUses(unary.Operand, name),
            BinaryExpression binary => CountVariableUses(binary.Left, name) + CountVariableUses(binary.Right, name),
            CallExpression call => call.Arguments.Sum(argument => CountVariableUses(argument, name)),
            _ => 0
        };

    private static bool IsSimpleInlineArgument(Expression expression)
        => expression is LiteralExpression { Value: I32Value } or VariableExpression;

    private static bool ContainsCall(Expression expression)
        => expression switch
        {
            CallExpression => true,
            UnaryExpression unary => ContainsCall(unary.Operand),
            BinaryExpression binary => ContainsCall(binary.Left) || ContainsCall(binary.Right),
            _ => false
        };
}
