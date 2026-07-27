using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler;

using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

internal static class IlEmitterPrimitives
{
    private static readonly ConcurrentDictionary<string, MethodInfo> RuntimeMethodCache = new();
    private static readonly OpCode[] Int32ShortOpcodes =
    [
        OpCodes.Ldc_I4_M1,
        OpCodes.Ldc_I4_0,
        OpCodes.Ldc_I4_1,
        OpCodes.Ldc_I4_2,
        OpCodes.Ldc_I4_3,
        OpCodes.Ldc_I4_4,
        OpCodes.Ldc_I4_5,
        OpCodes.Ldc_I4_6,
        OpCodes.Ldc_I4_7,
        OpCodes.Ldc_I4_8
    ];

    public static void EmitInt32(ILGenerator il, int value)
    {
        var shortIndex = value + 1;
        if ((uint)shortIndex < (uint)Int32ShortOpcodes.Length)
        {
            il.Emit(Int32ShortOpcodes[shortIndex]);
            return;
        }

        if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
        {
            il.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
            return;
        }

        il.Emit(OpCodes.Ldc_I4, value);
    }

    public static MethodInfo Runtime(string name)
        => RuntimeMethodCache.GetOrAdd(
            name,
            static key => typeof(Runtime.CompiledRuntime)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == key));

    // Execute rebuilds each expected input type on every invocation. Cache every eligible
    // built-in structural node while preserving the legacy EmitSandboxType behavior.
    public static void EmitEntrypointSandboxType(ILGenerator il, SandboxType type)
        => EmitSandboxType(il, type, allowCachedFactory: true);

    public static void EmitSandboxType(ILGenerator il, SandboxType type)
        => EmitSandboxType(il, type, allowCachedFactory: false);

    private static void EmitSandboxType(ILGenerator il, SandboxType type, bool allowCachedFactory)
    {
        if (type.Arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, type.Name);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.TypeScalar)));
            return;
        }

        if (type is { Name: "List", Arguments.Count: 1 })
        {
            EmitSandboxType(il, type.Arguments[0], allowCachedFactory);
            il.Emit(OpCodes.Call, Runtime(GetListFactory(type, allowCachedFactory)));
            return;
        }

        if (type is { Name: "Map", Arguments.Count: 2 })
        {
            EmitSandboxType(il, type.Arguments[0], allowCachedFactory);
            EmitSandboxType(il, type.Arguments[1], allowCachedFactory);
            il.Emit(OpCodes.Call, Runtime(GetMapFactory(type, allowCachedFactory)));
            return;
        }

        if (type.IsRecord)
        {
            // Build DotBoxD.Kernels.SandboxType[] via the trusted runtime facade (no newarr in verified IL),
            // populate each field type, then fold into a record type — mirrors the value-array path.
            il.Emit(OpCodes.Ldarg_0);
            EmitInt32(il, type.Arguments.Count);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.CreateMeteredTypeArray)));
            for (var i = 0; i < type.Arguments.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                EmitInt32(il, i);
                EmitSandboxType(il, type.Arguments[i], allowCachedFactory);
                il.Emit(OpCodes.Stelem_Ref);
            }

            var factory = allowCachedFactory && IsCacheableRecord(type)
                ? nameof(Kernels.Runtime.CompiledRuntime.TypeRecordCached)
                : nameof(Kernels.Runtime.CompiledRuntime.TypeRecord);
            il.Emit(OpCodes.Call, Runtime(factory));
            return;
        }

        throw new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.ValidationError,
            $"type '{type}' is not supported by compiler"));
    }

    private static string GetListFactory(SandboxType type, bool allowCachedFactory)
        => allowCachedFactory && IsBuiltinScalar(type.Arguments[0])
            ? nameof(Kernels.Runtime.CompiledRuntime.TypeListCached)
            : nameof(Kernels.Runtime.CompiledRuntime.TypeList);

    private static string GetMapFactory(SandboxType type, bool allowCachedFactory)
        => allowCachedFactory &&
           IsBuiltinScalar(type.Arguments[0]) &&
           IsBuiltinScalar(type.Arguments[1])
            ? nameof(Kernels.Runtime.CompiledRuntime.TypeMapCached)
            : nameof(Kernels.Runtime.CompiledRuntime.TypeMap);

    public static bool IsBuiltinScalar(SandboxType type)
        => type.Arguments.Count == 0 && type.IsKnownBuiltIn();

    public static bool IsCacheableRecord(SandboxType type)
    {
        if (!type.IsRecord || type.Arguments.Count is < 1 or > 2)
        {
            return false;
        }

        for (var i = 0; i < type.Arguments.Count; i++)
        {
            if (!IsBuiltinScalar(type.Arguments[i]))
            {
                return false;
            }
        }

        return true;
    }
}
