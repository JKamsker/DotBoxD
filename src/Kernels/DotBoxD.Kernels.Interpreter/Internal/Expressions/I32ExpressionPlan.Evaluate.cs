using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Expressions;

internal sealed partial class I32ExpressionPlan
{
    private static readonly Dictionary<ExpressionKind, PlanEvaluator> Evaluators = new()
    {
        [ExpressionKind.Literal] = static (plan, _, _) => plan._value,
        [ExpressionKind.RawVariable] = static (plan, frame, _) => frame.ReadRawInt32Slot(plan._value),
        [ExpressionKind.BoxedVariable] = static (plan, frame, _) => frame.ReadInt32Slot(plan._value),
        [ExpressionKind.Negate] = static (plan, frame, context) =>
            SandboxInt32Math.Negate(plan._left!.Evaluate(frame, context)),
        [ExpressionKind.InlineCall] = static (plan, frame, context) => plan.EvaluateInlineCall(frame, context),
        [ExpressionKind.RemainderAddRawRawConst] = static (plan, frame, _) => FastRemainder(
            SandboxInt32Math.Add(frame.ReadRawInt32Slot(plan._value), frame.ReadRawInt32Slot(plan._value2)),
            plan._value3,
            plan._magic),
        [ExpressionKind.RemainderAddRawConstConst] = static (plan, frame, _) => FastRemainder(
            SandboxInt32Math.Add(frame.ReadRawInt32Slot(plan._value), plan._value2),
            plan._value3,
            plan._magic),
        [ExpressionKind.RemainderByConst] = static (plan, frame, context) =>
            FastRemainder(plan._left!.Evaluate(frame, context), plan._value3, plan._magic),
        [ExpressionKind.DivideByConst] = static (plan, frame, context) =>
            FastDivide(plan._left!.Evaluate(frame, context), plan._value3, plan._magic),
        [ExpressionKind.AddRawMultiplyRawConst] = static (plan, frame, _) => SandboxInt32Math.Add(
            frame.ReadRawInt32Slot(plan._value),
            SandboxInt32Math.Multiply(frame.ReadRawInt32Slot(plan._value2), plan._value3)),
        [ExpressionKind.Add] = static (plan, frame, context) =>
            SandboxInt32Math.Add(plan._left!.Evaluate(frame, context), plan._right!.Evaluate(frame, context)),
        [ExpressionKind.Subtract] = static (plan, frame, context) =>
            SandboxInt32Math.Subtract(plan._left!.Evaluate(frame, context), plan._right!.Evaluate(frame, context)),
        [ExpressionKind.Multiply] = static (plan, frame, context) =>
            SandboxInt32Math.Multiply(plan._left!.Evaluate(frame, context), plan._right!.Evaluate(frame, context)),
        [ExpressionKind.Divide] = static (plan, frame, context) =>
            SandboxInt32Math.Divide(plan._left!.Evaluate(frame, context), plan._right!.Evaluate(frame, context)),
        [ExpressionKind.Remainder] = static (plan, frame, context) =>
            SandboxInt32Math.Remainder(plan._left!.Evaluate(frame, context), plan._right!.Evaluate(frame, context)),
    };

    public int Evaluate(InterpreterFrame frame, SandboxContext context)
        => Evaluators.TryGetValue(_kind, out var evaluator)
            ? evaluator(this, frame, context)
            : throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"));

    private delegate int PlanEvaluator(
        I32ExpressionPlan plan,
        InterpreterFrame frame,
        SandboxContext context);
}
