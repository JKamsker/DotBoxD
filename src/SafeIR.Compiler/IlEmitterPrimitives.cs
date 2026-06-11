namespace SafeIR.Compiler;

using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;

internal static class IlEmitterPrimitives
{
    public static void EmitInt32(ILGenerator il, int value)
    {
        switch (value) {
            case -1:
                il.Emit(OpCodes.Ldc_I4_M1);
                break;
            case 0:
                il.Emit(OpCodes.Ldc_I4_0);
                break;
            case 1:
                il.Emit(OpCodes.Ldc_I4_1);
                break;
            case 2:
                il.Emit(OpCodes.Ldc_I4_2);
                break;
            case 3:
                il.Emit(OpCodes.Ldc_I4_3);
                break;
            case 4:
                il.Emit(OpCodes.Ldc_I4_4);
                break;
            case 5:
                il.Emit(OpCodes.Ldc_I4_5);
                break;
            case 6:
                il.Emit(OpCodes.Ldc_I4_6);
                break;
            case 7:
                il.Emit(OpCodes.Ldc_I4_7);
                break;
            case 8:
                il.Emit(OpCodes.Ldc_I4_8);
                break;
            case >= sbyte.MinValue and <= sbyte.MaxValue:
                il.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
                break;
            default:
                il.Emit(OpCodes.Ldc_I4, value);
                break;
        }
    }

    public static MethodInfo Runtime(string name)
        => typeof(CompiledRuntime).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == name);
}
