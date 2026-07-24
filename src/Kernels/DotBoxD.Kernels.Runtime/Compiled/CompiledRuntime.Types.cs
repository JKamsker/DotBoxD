using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Runtime;

public static partial class CompiledRuntime
{
    public static SandboxType TypeScalar(string name)
        => name switch
        {
            "Unit" => SandboxType.Unit,
            "Bool" => SandboxType.Bool,
            "I32" => SandboxType.I32,
            "I64" => SandboxType.I64,
            "F64" => SandboxType.F64,
            "String" => SandboxType.String,
            "Guid" => SandboxType.Guid,
            _ => TypePathOrOpaqueScalar(name)
        };

    private static SandboxType TypePathOrOpaqueScalar(string name)
        => name switch
        {
            "SandboxPath" => SandboxType.SandboxPath,
            "SandboxUri" => SandboxType.SandboxUri,
            _ => SandboxType.Scalar(name)
        };

    public static SandboxType TypeList(SandboxType itemType) => SandboxType.List(itemType);

    public static SandboxType TypeMap(SandboxType keyType, SandboxType valueType) => SandboxType.Map(keyType, valueType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SandboxType TypeListCached(SandboxType itemType)
        => CompiledStructuralTypeCache.List(itemType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SandboxType TypeMapCached(SandboxType keyType, SandboxType valueType)
        => CompiledStructuralTypeCache.Map(keyType, valueType);

    public static SandboxType TypeRecord(SandboxType[] fieldTypes) => SandboxType.Record(fieldTypes);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SandboxType TypeRecordCached(SandboxType[] fieldTypes)
        => CompiledStructuralTypeCache.Record(fieldTypes);

    public static SandboxType[] CreateMeteredTypeArray(SandboxContext context, int count)
    {
        ChargeTypeArray(context, count);
        return new SandboxType[count];
    }

    public static SandboxType[] CreateTypeArray(int count)
        => count >= 0 ? new SandboxType[count] : throw InvalidInput("type array length must be non-negative");

    /// <summary>
    /// Generated-code ABI helper that validates a selected structural entrypoint return and records a
    /// single-use proof for the host. The validation happens before publication, so hand-written or custom
    /// compiled artifacts cannot use the proof to bypass the sandbox value boundary.
    /// </summary>
    public static SandboxValue RequireValueTypeAndRecordValidation(
        SandboxContext context,
        SandboxValue value,
        SandboxType expectedType)
    {
        ArgumentNullException.ThrowIfNull(context);
        EntrypointBinder.RequireType(value, expectedType, "function return type mismatch");
        // Empty list/map validation is already O(1); publishing a proof would cost more than the host's
        // fallback check. Non-empty collections and records avoid a genuine second structural walk.
        if (value is not ListValue { Count: 0 } and not MapValue { Values.Count: 0 })
        {
            context.RecordCompiledReturnValidation(value, expectedType);
        }

        return value;
    }

    private static void ChargeTypeArray(SandboxContext context, int count)
    {
        if (count < 0)
        {
            throw InvalidInput("type array length must be non-negative");
        }

        var elementCount = Math.Max(1L, count);
        context.ChargeFuel(elementCount);
        context.ChargeAllocation(checked(elementCount * 8));
    }
}
