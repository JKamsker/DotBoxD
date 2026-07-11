using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Expressions;

internal sealed partial class I32ExpressionPlan
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public int Evaluate(InterpreterFrame frame, SandboxContext context)
    {
        if (_kind <= ExpressionKind.BoxedVariable)
        {
            return EvaluateLeaf(frame);
        }

        if (_kind <= ExpressionKind.AddRawMultiplyRawConst)
        {
            return EvaluateSpecial(frame, context);
        }

        return EvaluateBinary(frame, context);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private int EvaluateLeaf(InterpreterFrame frame)
        => _kind switch
        {
            ExpressionKind.Literal => _value,
            ExpressionKind.RawVariable => frame.ReadRawInt32Slot(_value),
            ExpressionKind.BoxedVariable => frame.ReadInt32Slot(_value),
            _ => throw UnsupportedExpression()
        };

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private int EvaluateSpecial(InterpreterFrame frame, SandboxContext context)
        => _kind switch
        {
            ExpressionKind.Negate => SandboxInt32Math.Negate(_left!.Evaluate(frame, context)),
            ExpressionKind.InlineCall => EvaluateInlineCall(frame, context),
            ExpressionKind.RemainderAddRawRawConst => FastRemainder(
                SandboxInt32Math.Add(frame.ReadRawInt32Slot(_value), frame.ReadRawInt32Slot(_value2)),
                _value3,
                _magic),
            ExpressionKind.RemainderAddRawConstConst => FastRemainder(
                SandboxInt32Math.Add(frame.ReadRawInt32Slot(_value), _value2),
                _value3,
                _magic),
            ExpressionKind.RemainderByConst => FastRemainder(_left!.Evaluate(frame, context), _value3, _magic),
            ExpressionKind.DivideByConst => FastDivide(_left!.Evaluate(frame, context), _value3, _magic),
            ExpressionKind.AddRawMultiplyRawConst => SandboxInt32Math.Add(
                frame.ReadRawInt32Slot(_value),
                SandboxInt32Math.Multiply(frame.ReadRawInt32Slot(_value2), _value3)),
            _ => throw UnsupportedExpression()
        };

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private int EvaluateBinary(InterpreterFrame frame, SandboxContext context)
        => _kind switch
        {
            ExpressionKind.Add => SandboxInt32Math.Add(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            ExpressionKind.Subtract => SandboxInt32Math.Subtract(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            ExpressionKind.Multiply => SandboxInt32Math.Multiply(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            ExpressionKind.Divide => SandboxInt32Math.Divide(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            ExpressionKind.Remainder => SandboxInt32Math.Remainder(_left!.Evaluate(frame, context), _right!.Evaluate(frame, context)),
            _ => throw UnsupportedExpression()
        };

    private static SandboxRuntimeException UnsupportedExpression()
        => new(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"));
}
