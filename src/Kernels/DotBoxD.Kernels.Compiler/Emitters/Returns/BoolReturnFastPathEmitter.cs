namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class BoolReturnFastPathEmitter
{
    public static bool TryEmit(
        Expression expression,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        if (expression is not BinaryExpression { Operator: "&&" } and ||
            !TryGetStringEquals(and.Left, stackPlan, out var leftString, out var rightString) ||
            !TryGetI32GreaterOrEqual(and.Right, stackPlan, out var leftI32, out var rightI32))
        {
            return false;
        }

        EmitStringAndI32Gte(il, declare, leftString, rightString, leftI32, rightI32);
        return true;
    }

    private static void EmitStringAndI32Gte(
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        string leftString,
        string rightString,
        string leftI32,
        string rightI32)
    {
        var falseLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        CompiledMeterEmitter.Fuel(il, 4);
        il.Emit(OpCodes.Ldloc, declare(leftString).Local);
        il.Emit(OpCodes.Ldloc, declare(rightString).Local);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.StringEqualsRaw)));
        il.Emit(OpCodes.Brfalse, falseLabel);

        CompiledMeterEmitter.Fuel(il, 3);
        il.Emit(OpCodes.Ldloc, declare(leftI32).Local);
        il.Emit(OpCodes.Ldloc, declare(rightI32).Local);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.GteI32Raw)));
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.Bool)));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(falseLabel);
        EmitInt32(il, 0);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.Bool)));
        il.MarkLabel(endLabel);
    }

    private static bool TryGetStringEquals(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        out string left,
        out string right)
    {
        left = "";
        right = "";
        return expression is BinaryExpression { Operator: "==" } equals &&
               TryGetVariable(equals.Left, SandboxType.String, stackPlan, out left) &&
               TryGetVariable(equals.Right, SandboxType.String, stackPlan, out right);
    }

    private static bool TryGetI32GreaterOrEqual(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        out string left,
        out string right)
    {
        left = "";
        right = "";
        return expression is BinaryExpression { Operator: ">=" } comparison &&
               TryGetVariable(comparison.Left, SandboxType.I32, stackPlan, out left) &&
               TryGetVariable(comparison.Right, SandboxType.I32, stackPlan, out right);
    }

    private static bool TryGetVariable(
        Expression expression,
        SandboxType type,
        LocalStackKindPlanner stackPlan,
        out string name)
    {
        if (expression is VariableExpression variable &&
            stackPlan.Infer(variable) == type)
        {
            name = variable.Name;
            return true;
        }

        name = "";
        return false;
    }
}
