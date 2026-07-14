using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class CompiledTypeEmitter
{
    public static void EmitMetered(ILGenerator il, SandboxType type)
        => EmitMetered(il, type, allowCachedFactory: true);

    private static void EmitMetered(ILGenerator il, SandboxType type, bool allowCachedFactory)
    {
        if (type is { Name: "List", Arguments.Count: 1 })
        {
            EmitMetered(il, type.Arguments[0], allowCachedFactory: false);
            CompiledMeterEmitter.Fuel(il, 1);
            var factory = allowCachedFactory && IsBuiltinScalar(type.Arguments[0])
                ? nameof(Kernels.Runtime.CompiledRuntime.TypeListCached)
                : nameof(Kernels.Runtime.CompiledRuntime.TypeList);
            il.Emit(OpCodes.Call, Runtime(factory));
            return;
        }

        if (type is { Name: "Map", Arguments.Count: 2 })
        {
            EmitMetered(il, type.Arguments[0], allowCachedFactory: false);
            EmitMetered(il, type.Arguments[1], allowCachedFactory: false);
            CompiledMeterEmitter.Fuel(il, 1);
            var factory = allowCachedFactory &&
                          IsBuiltinScalar(type.Arguments[0]) &&
                          IsBuiltinScalar(type.Arguments[1])
                ? nameof(Kernels.Runtime.CompiledRuntime.TypeMapCached)
                : nameof(Kernels.Runtime.CompiledRuntime.TypeMap);
            il.Emit(OpCodes.Call, Runtime(factory));
            return;
        }

        if (type.IsRecord)
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitInt32(il, type.Arguments.Count);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.CreateMeteredTypeArray)));
            for (var i = 0; i < type.Arguments.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                EmitInt32(il, i);
                EmitMetered(il, type.Arguments[i], allowCachedFactory: false);
                il.Emit(OpCodes.Stelem_Ref);
            }

            CompiledMeterEmitter.Fuel(il, 1);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.TypeRecord)));
            return;
        }

        CompiledMeterEmitter.Fuel(il, 1);
        il.Emit(OpCodes.Ldstr, type.Name);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.TypeScalar)));
    }

    private static bool IsBuiltinScalar(SandboxType type)
        => type.Arguments.Count == 0 && type.IsKnownBuiltIn();
}
