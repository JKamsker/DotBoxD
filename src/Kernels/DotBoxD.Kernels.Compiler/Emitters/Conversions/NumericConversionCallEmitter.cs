using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class NumericConversionCallEmitter
{
    private static readonly RawConversion[] RawConversions =
    [
        new(StackKind.I64, "numeric.toI64", SandboxType.I32, StackKind.I32, OpCodes.Conv_I8),
        new(StackKind.F64, "numeric.toF64", SandboxType.I32, StackKind.I32, OpCodes.Conv_R8),
        new(StackKind.F64, "numeric.toF64", SandboxType.I64, StackKind.I64, OpCodes.Conv_R8)
    ];

    public static bool TryEmitRaw(
        Expression expression,
        StackKind target,
        LocalStackKindPlanner stackPlan,
        ILGenerator il,
        Action<Expression, StackKind> emitAs)
    {
        if (expression is not CallExpression call || call.Arguments.Count != 1)
        {
            return false;
        }

        var argument = call.Arguments[0];
        var sourceType = stackPlan.Infer(argument);
        foreach (var conversion in RawConversions)
        {
            if (conversion.Matches(target, call.Name, sourceType))
            {
                EmitRawConversion(argument, conversion, il, emitAs);
                return true;
            }
        }

        return false;
    }

    private static void EmitRawConversion(
        Expression argument,
        RawConversion conversion,
        ILGenerator il,
        Action<Expression, StackKind> emitAs)
    {
        CompiledMeterEmitter.Fuel(il, 1);
        emitAs(argument, conversion.SourceStack);
        il.Emit(conversion.OpCode);
    }

    public static bool TryEmit(
        CallExpression call,
        LocalStackKindPlanner stackPlan,
        ILGenerator il,
        Action<Expression, StackKind> emitAs)
    {
        if (call.Arguments.Count != 1)
        {
            return false;
        }

        var argument = call.Arguments[0];
        var sourceType = stackPlan.Infer(argument);
        switch (call.Name)
        {
            case "numeric.toI64" when sourceType == SandboxType.I32:
                emitAs(argument, StackKind.I32);
                il.Emit(OpCodes.Conv_I8);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.I64)));
                return true;
            case "numeric.toF64" when sourceType == SandboxType.I32:
                emitAs(argument, StackKind.I32);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.F64)));
                return true;
            case "numeric.toF64" when sourceType == SandboxType.I64:
                emitAs(argument, StackKind.I64);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.F64)));
                return true;
            case "numeric.toI64":
            case "numeric.toF64":
                throw Unsupported($"conversion '{call.Name}' from {sourceType} is not supported by compiler");
            default:
                return false;
        }
    }

    private static Exception Unsupported(string message)
        => new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, message));

    private readonly record struct RawConversion(
        StackKind Target,
        string Name,
        SandboxType SourceType,
        StackKind SourceStack,
        OpCode OpCode)
    {
        public bool Matches(StackKind target, string name, SandboxType? sourceType)
            => Target == target &&
               string.Equals(Name, name, StringComparison.Ordinal) &&
               sourceType is not null &&
               SourceType.Equals(sourceType);
    }
}
