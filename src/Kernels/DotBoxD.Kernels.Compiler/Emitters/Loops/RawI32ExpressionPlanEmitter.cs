using System.Reflection.Emit;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal static class RawI32ExpressionPlanEmitter
{
    public static void Emit(
        RawI32ExpressionPlan plan,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        if (TryEmitTerminal(plan, il, declare) ||
            TryEmitUnaryOrIntrinsic(plan, il, declare) ||
            TryEmitArithmetic(plan, il, declare))
        {
            return;
        }
    }

    public static int BaseInstructionCost(RawI32ExpressionPlan.ExpressionKind kind)
        => kind is RawI32ExpressionPlan.ExpressionKind.Abs
            or RawI32ExpressionPlan.ExpressionKind.Min
            or RawI32ExpressionPlan.ExpressionKind.Max
            or RawI32ExpressionPlan.ExpressionKind.Clamp
            ? 4
            : 1;

    private static bool TryEmitTerminal(
        RawI32ExpressionPlan plan,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        switch (plan.Kind)
        {
            case RawI32ExpressionPlan.ExpressionKind.Literal:
                EmitInt32(il, plan.Literal);
                return true;
            case RawI32ExpressionPlan.ExpressionKind.Variable:
                il.Emit(OpCodes.Ldloc, declare(plan.Name!).Local);
                return true;
            default:
                return false;
        }
    }

    private static bool TryEmitUnaryOrIntrinsic(
        RawI32ExpressionPlan plan,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        switch (plan.Kind)
        {
            case RawI32ExpressionPlan.ExpressionKind.Negate:
                plan.Left!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.NegI32Raw)));
                return true;
            case RawI32ExpressionPlan.ExpressionKind.Abs:
                plan.Left!.Emit(il, declare);
                EmitChargeBindingCall(il, plan.Name!);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AbsI32Raw)));
                return true;
            case RawI32ExpressionPlan.ExpressionKind.Min:
            case RawI32ExpressionPlan.ExpressionKind.Max:
                plan.Left!.Emit(il, declare);
                plan.Right!.Emit(il, declare);
                EmitChargeBindingCall(il, plan.Name!);
                il.Emit(OpCodes.Call, Runtime(RuntimeMethod(plan.Kind)));
                return true;
            case RawI32ExpressionPlan.ExpressionKind.Clamp:
                plan.Left!.Emit(il, declare);
                plan.Right!.Emit(il, declare);
                plan.Third!.Emit(il, declare);
                EmitChargeBindingCall(il, plan.Name!);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ClampI32Raw)));
                return true;
            case RawI32ExpressionPlan.ExpressionKind.InlineCall:
                EmitInlineCall(plan, il, declare);
                return true;
            default:
                return false;
        }
    }

    private static void EmitInlineCall(
        RawI32ExpressionPlan plan,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.EnterInlineCall)));
        plan.Left!.Emit(il, declare);
        var value = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ExitInlineCall)));
        il.Emit(OpCodes.Ldloc, value);
    }

    private static bool TryEmitArithmetic(
        RawI32ExpressionPlan plan,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        switch (plan.Kind)
        {
            case RawI32ExpressionPlan.ExpressionKind.Add:
            case RawI32ExpressionPlan.ExpressionKind.Subtract:
            case RawI32ExpressionPlan.ExpressionKind.Multiply:
            case RawI32ExpressionPlan.ExpressionKind.Divide:
            case RawI32ExpressionPlan.ExpressionKind.Remainder:
                plan.Left!.Emit(il, declare);
                plan.Right!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(RuntimeMethod(plan.Kind)));
                return true;
            case RawI32ExpressionPlan.ExpressionKind.AddRemainder:
                plan.Left!.Emit(il, declare);
                plan.Right!.Emit(il, declare);
                plan.Third!.Emit(il, declare);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddRemI32Raw)));
                return true;
            default:
                return false;
        }
    }

    private static void EmitChargeBindingCall(ILGenerator il, string bindingId)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, bindingId);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeBindingCall)));
    }

    private static string RuntimeMethod(RawI32ExpressionPlan.ExpressionKind kind)
        => kind switch
        {
            RawI32ExpressionPlan.ExpressionKind.Add => nameof(CompiledRuntime.AddI32Raw),
            RawI32ExpressionPlan.ExpressionKind.Subtract => nameof(CompiledRuntime.SubI32Raw),
            RawI32ExpressionPlan.ExpressionKind.Multiply => nameof(CompiledRuntime.MulI32Raw),
            RawI32ExpressionPlan.ExpressionKind.Divide => nameof(CompiledRuntime.DivI32Raw),
            RawI32ExpressionPlan.ExpressionKind.Remainder => nameof(CompiledRuntime.RemI32Raw),
            RawI32ExpressionPlan.ExpressionKind.Min => nameof(CompiledRuntime.MinI32Raw),
            RawI32ExpressionPlan.ExpressionKind.Max => nameof(CompiledRuntime.MaxI32Raw),
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
        };
}
