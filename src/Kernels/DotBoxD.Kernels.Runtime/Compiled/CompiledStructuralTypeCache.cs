using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

internal static class CompiledStructuralTypeCache
{
    private const int BuiltinScalarCount = 9;
    private static readonly SandboxType?[] s_listTypes = new SandboxType?[BuiltinScalarCount];
    private static readonly SandboxType?[] s_mapTypes = new SandboxType?[BuiltinScalarCount * BuiltinScalarCount];

    public static SandboxType List(SandboxType itemType)
    {
        if (itemType is null)
        {
            return SandboxType.List(itemType!);
        }

        var index = BuiltinScalarIndex(itemType);
        if (index < 0)
        {
            return SandboxType.List(itemType);
        }

        var cached = Volatile.Read(ref s_listTypes[index]);
        if (cached is not null)
        {
            return cached;
        }

        var created = SandboxType.List(itemType);
        return Interlocked.CompareExchange(ref s_listTypes[index], created, null) ?? created;
    }

    public static SandboxType Map(SandboxType keyType, SandboxType valueType)
    {
        if (keyType is null || valueType is null)
        {
            return SandboxType.Map(keyType!, valueType!);
        }

        var keyIndex = BuiltinScalarIndex(keyType);
        var valueIndex = BuiltinScalarIndex(valueType);
        if (keyIndex < 0 || valueIndex < 0)
        {
            return SandboxType.Map(keyType, valueType);
        }

        var index = (keyIndex * BuiltinScalarCount) + valueIndex;
        var cached = Volatile.Read(ref s_mapTypes[index]);
        if (cached is not null)
        {
            return cached;
        }

        var created = SandboxType.Map(keyType, valueType);
        return Interlocked.CompareExchange(ref s_mapTypes[index], created, null) ?? created;
    }

    public static SandboxType Record(SandboxType[] fieldTypes)
    {
        if (fieldTypes is null || fieldTypes.Length is < 1 or > 2)
        {
            return SandboxType.Record(fieldTypes!);
        }

        // Snapshot every mutable array element before lookup or publication. Generated IL owns its array,
        // but this runtime facade is public for generated-code ABI reasons, so a hand-written caller can
        // mutate an input array concurrently. A cached descriptor must never observe a later mutation or be
        // published under an index derived from different fields than the descriptor contains.
        var firstFieldType = fieldTypes[0];
        if (firstFieldType is null)
        {
            return SandboxType.Record(fieldTypes);
        }

        var firstIndex = BuiltinScalarIndex(firstFieldType);
        if (firstIndex < 0)
        {
            return SandboxType.Record(fieldTypes);
        }

        if (fieldTypes.Length == 1)
        {
            return OneFieldRecord(firstFieldType, firstIndex);
        }

        var secondFieldType = fieldTypes[1];
        if (secondFieldType is null)
        {
            return SandboxType.Record(fieldTypes);
        }

        var secondIndex = BuiltinScalarIndex(secondFieldType);
        return secondIndex < 0
            ? SandboxType.Record(fieldTypes)
            : TwoFieldRecord(firstFieldType, secondFieldType, firstIndex, secondIndex);
    }

    private static SandboxType OneFieldRecord(SandboxType fieldType, int index)
    {
        var cached = Volatile.Read(ref RecordCache.OneFieldTypes[index]);
        if (cached is not null)
        {
            return cached;
        }

        var created = SandboxType.Record([fieldType]);
        return Interlocked.CompareExchange(ref RecordCache.OneFieldTypes[index], created, null) ?? created;
    }

    private static SandboxType TwoFieldRecord(
        SandboxType firstFieldType,
        SandboxType secondFieldType,
        int firstIndex,
        int secondIndex)
    {
        var index = (firstIndex * BuiltinScalarCount) + secondIndex;
        var cached = Volatile.Read(ref RecordCache.TwoFieldTypes[index]);
        if (cached is not null)
        {
            return cached;
        }

        var created = SandboxType.Record([firstFieldType, secondFieldType]);
        return Interlocked.CompareExchange(ref RecordCache.TwoFieldTypes[index], created, null) ?? created;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BuiltinScalarIndex(SandboxType type)
    {
        if (type.Arguments.Count != 0)
        {
            return -1;
        }

        return type.Name.Length <= 4
            ? ShortBuiltinScalarIndex(type)
            : LongBuiltinScalarIndex(type);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ShortBuiltinScalarIndex(SandboxType type)
        => type.Name.Length switch
        {
            3 => ThreeCharacterScalarIndex(type),
            4 => FourCharacterScalarIndex(type),
            _ => -1
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LongBuiltinScalarIndex(SandboxType type)
        => type.Name.Length switch
        {
            6 => ReferenceEquals(type, SandboxType.String) ? 5 : -1,
            10 => ReferenceEquals(type, SandboxType.SandboxUri) ? 8 : -1,
            11 => ReferenceEquals(type, SandboxType.SandboxPath) ? 7 : -1,
            _ => -1
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ThreeCharacterScalarIndex(SandboxType type)
        => ReferenceEquals(type, SandboxType.I32)
            ? 2
            : ReferenceEquals(type, SandboxType.I64)
                ? 3
                : ReferenceEquals(type, SandboxType.F64) ? 4 : -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FourCharacterScalarIndex(SandboxType type)
        => ReferenceEquals(type, SandboxType.Unit)
            ? 0
            : ReferenceEquals(type, SandboxType.Bool)
                ? 1
                : ReferenceEquals(type, SandboxType.Guid) ? 6 : -1;

    private static class RecordCache
    {
        public static readonly SandboxType?[] OneFieldTypes = new SandboxType?[BuiltinScalarCount];
        public static readonly SandboxType?[] TwoFieldTypes =
            new SandboxType?[BuiltinScalarCount * BuiltinScalarCount];
    }
}
