using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter;

internal readonly partial struct ExpressionEvaluator
{
    public bool TryEvaluateInlineInt32Call(
        Expression expression,
        InterpreterFrame frame,
        out int value,
        out SandboxFunction? genericFunction)
        => InlineI32LocalFunctionCallEvaluator.TryEvaluate(
            expression,
            frame,
            _interpreter,
            out value,
            out genericFunction);

    internal static bool CanReuseResolvedLocalCall(CallExpression call)
        => ResolveCallPrecedence(call) == CallPrecedence.LocalOrBinding;

    internal ValueTask<SandboxValue> EvaluateResolvedLocalCall(
        CallExpression call,
        SandboxFunction function,
        InterpreterFrame frame)
    {
        Context.ChargeFuel(1);
        return EvaluateLocalFunctionCall(call, function, frame);
    }

    private static CallPrecedence ResolveCallPrecedence(CallExpression call)
    {
        if (UnaryPureIntrinsicDispatcher.IsCandidate(call.Name))
        {
            return CallPrecedence.UnaryIntrinsic;
        }

        if (IsNumericConversion(call.Name) && call.Arguments.Count == 1)
        {
            return CallPrecedence.NumericConversion;
        }

        var fixedArity = CollectionIntrinsicDispatcher.FixedArity(call.Name);
        if (fixedArity >= 0 && fixedArity == call.Arguments.Count)
        {
            return CallPrecedence.FixedCollection;
        }

        return CollectionCalls.ContainsKey(call.Name)
            ? CallPrecedence.ArrayCollection
            : CallPrecedence.LocalOrBinding;
    }

    private enum CallPrecedence
    {
        UnaryIntrinsic,
        NumericConversion,
        FixedCollection,
        ArrayCollection,
        LocalOrBinding
    }
}
