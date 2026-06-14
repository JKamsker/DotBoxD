namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

/// <summary>
/// Emits strided loop metering (exp/strided-metering): instead of a non-inlinable per-iteration
/// <c>CompiledRuntime.ChargeLoopIteration</c> call (the dominant compiled-loop cost), the loop counts
/// iterations in a generated-method local and flushes the batch via <see cref="CompiledRuntime.ChargeLoopBatch"/>
/// every <see cref="Stride"/> iterations and once at loop exit. Charged loop-iterations/fuel totals are
/// identical; the per-iteration cross-assembly call is removed.
/// </summary>
internal static class CompiledLoopMeter
{
    public const int Stride = 1024;

    /// <summary>Declares the iteration counter local and zero-initializes it before the loop.</summary>
    public static LocalBuilder Begin(ILGenerator il)
    {
        var counter = il.DeclareLocal(typeof(int));
        EmitInt32(il, 0);
        il.Emit(OpCodes.Stloc, counter);
        return counter;
    }

    /// <summary>
    /// At the top of a guaranteed-executed iteration: <c>if (++counter == Stride) { ChargeLoopBatch(ctx,
    /// Stride, fuel); counter = 0; }</c>. Counts in arrears so the iteration count stays exact.
    /// </summary>
    public static void Tick(ILGenerator il, LocalBuilder counter, int fuelPerIteration)
    {
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, counter);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, counter);
        il.Emit(OpCodes.Ldloc, counter);
        EmitInt32(il, Stride);
        il.Emit(OpCodes.Blt, skip);
        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, Stride);
        EmitInt32(il, fuelPerIteration);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeLoopBatch)));
        EmitInt32(il, 0);
        il.Emit(OpCodes.Stloc, counter);
        il.MarkLabel(skip);
    }

    /// <summary>On the loop-exit path: <c>if (counter != 0) ChargeLoopBatch(ctx, counter, fuel);</c>.</summary>
    public static void Flush(ILGenerator il, LocalBuilder counter, int fuelPerIteration)
    {
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, counter);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, counter);
        EmitInt32(il, fuelPerIteration);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeLoopBatch)));
        il.MarkLabel(skip);
    }
}
