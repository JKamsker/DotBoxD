using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static readonly Dictionary<Type, SandboxType> ExactSandboxTypes = new()
    {
        [typeof(bool)] = SandboxType.Bool,
        [typeof(int)] = SandboxType.I32,
        [typeof(long)] = SandboxType.I64,
        [typeof(double)] = SandboxType.F64,
        // float widens losslessly to the sandbox's only floating kind (F64); decode narrows back exactly.
        [typeof(float)] = SandboxType.F64,
        [typeof(string)] = SandboxType.String,
        [typeof(Guid)] = SandboxType.Guid,
        [typeof(DateOnly)] = SandboxType.I32,
        [typeof(TimeOnly)] = SandboxType.I64,
        [typeof(TimeSpan)] = SandboxType.I64,
        [typeof(CancellationToken)] = SandboxType.Bool,
    };

    private static readonly FrameworkSandboxType[] FrameworkSandboxTypes =
    [
        new(IsDateTimeWireType, CreateDateTimeWireSandboxType),
        new(IsDecimalWireType, CreateDecimalWireSandboxType),
        new(static type => type == typeof(Index), CreateIndexWireSandboxType),
        new(static type => type == typeof(Range), CreateRangeWireSandboxType),
    ];

    public static SandboxType SandboxTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return SandboxTypeOf(type, 0, rejectNullableReferences: true);
    }

    internal static SandboxType HookResultSandboxTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return SandboxTypeOf(type, 0, rejectNullableReferences: false);
    }

    // The list/map/record nesting depth is bounded so a self-referential DTO (e.g. a class with a property of
    // its own type) fails with a catchable NotSupportedException instead of an uncatchable StackOverflowException
    // when, say, ConventionEventAdapter is constructed for it. Kept at or below the kernel verifier's structural
    // depth limit (SandboxType.IsKnown defaults to maxDepth 8) so a produced type is never rejected at install.
    private const int MaxTypeNestingDepth = 8;

    private static SandboxType SandboxTypeOf(Type type, int depth)
        => SandboxTypeOf(type, depth, rejectNullableReferences: true);

    private static SandboxType SandboxTypeOf(Type type, int depth, bool rejectNullableReferences)
    {
        if (TryNullableSandboxType(type, depth, out var nullable))
        {
            return nullable;
        }

        if (TryExactSandboxType(type, out var exact))
        {
            return exact;
        }

        if (TryFrameworkSandboxType(type, depth, out var framework))
        {
            return framework;
        }

        if (type.IsEnum)
        {
            return EnumUsesI64(type) ? SandboxType.I64 : SandboxType.I32;
        }

        ThrowIfUnsupportedFrameworkStruct(type);
        RejectNestedTypeTooDeep(type, depth);

        if (ElementType(type) is { } elementType)
        {
            return SandboxType.List(SandboxTypeOf(elementType, depth + 1, rejectNullableReferences));
        }

        if (MapTypes(type) is { } mapTypes)
        {
            return MapSandboxType(mapTypes, depth, rejectNullableReferences);
        }

        if (DtoShape(type) is { } shape)
        {
            return DtoSandboxType(type, shape, depth, rejectNullableReferences);
        }

        throw new NotSupportedException($"Server extension has no sandbox type for '{type}'.");
    }

    private static bool TryExactSandboxType(Type type, out SandboxType sandboxType)
    {
        if (ExactSandboxTypes.TryGetValue(type, out var exact))
        {
            sandboxType = exact;
            return true;
        }

        sandboxType = null!;
        return false;
    }

    private static bool TryFrameworkSandboxType(Type type, int depth, out SandboxType sandboxType)
    {
        foreach (var candidate in FrameworkSandboxTypes)
        {
            if (candidate.Matches(type))
            {
                sandboxType = candidate.Create(type, depth);
                return true;
            }
        }

        sandboxType = null!;
        return false;
    }

    private static SandboxType MapSandboxType(
        (Type Key, Type Value) mapTypes,
        int depth,
        bool rejectNullableReferences)
    {
        RejectUnsupportedMapKeyType(mapTypes.Key);
        var keyType = SandboxTypeOf(mapTypes.Key, depth + 1, rejectNullableReferences);
        // The kernel verifier only accepts a fixed set of scalar map keys (bool/int/long/string/opaque-id, not
        // Guid or double). Reject an unsupported key here with a catchable NotSupportedException instead of
        // producing a Map<Guid,V> that later fails IsKnown validation at install.
        if (!keyType.IsValidMapKey())
        {
            throw new NotSupportedException(
                $"Kernel RPC service map key type '{mapTypes.Key}' is not a supported sandbox map key.");
        }

        return SandboxType.Map(keyType, SandboxTypeOf(mapTypes.Value, depth + 1, rejectNullableReferences));
    }

    private static SandboxType DtoSandboxType(
        Type type,
        RecordShape shape,
        int depth,
        bool rejectNullableReferences)
    {
        shape.RejectUnmatchedRequiredConstructor();
        if (rejectNullableReferences)
        {
            RejectNullableReferenceDtoShape(type, shape);
        }

        var fields = shape.Fields;
        var fieldTypes = new SandboxType[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            fieldTypes[i] = SandboxTypeOf(fields[i].Type, depth + 1, rejectNullableReferences);
        }

        return SandboxType.Record(fieldTypes);
    }

    private static SandboxType CreateDateTimeWireSandboxType(Type type, int depth)
    {
        RejectRecordTypeTooDeep(type, depth);
        return DateTimeWireSandboxType();
    }

    private static SandboxType CreateDecimalWireSandboxType(Type type, int depth)
    {
        RejectRecordTypeTooDeep(type, depth);
        return DecimalWireSandboxType();
    }

    private static SandboxType CreateIndexWireSandboxType(Type type, int depth)
    {
        RejectRecordTypeTooDeep(type, depth);
        return IndexWireSandboxType();
    }

    private static SandboxType CreateRangeWireSandboxType(Type type, int depth)
    {
        RejectRangeTypeTooDeep(type, depth);
        return RangeWireSandboxType();
    }

    private static void RejectUnsupportedMapKeyType(Type keyType)
    {
        if (keyType == typeof(CancellationToken))
        {
            throw new NotSupportedException(
                "Kernel RPC service map key type 'System.Threading.CancellationToken' is not supported; " +
                "CancellationToken marshals as a bool snapshot and would collapse distinct tokens.");
        }
    }

    private static void ThrowIfUnsupportedFrameworkStruct(Type type)
    {
        if (IsFrameworkStructWireType(type))
        {
            throw new NotSupportedException($"Server extension has no sandbox type for '{type}'.");
        }
    }

    private static void RejectNestedTypeTooDeep(Type type, int depth)
    {
        if (depth >= MaxTypeNestingDepth)
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' nests beyond the supported depth of {MaxTypeNestingDepth}.");
        }
    }

    private static void RejectRecordTypeTooDeep(Type type, int depth)
    {
        if (depth >= MaxTypeNestingDepth)
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' nests beyond the supported depth of {MaxTypeNestingDepth}.");
        }
    }

    private static void RejectRangeTypeTooDeep(Type type, int depth)
    {
        if (depth + 1 >= MaxTypeNestingDepth)
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' nests beyond the supported depth of {MaxTypeNestingDepth}.");
        }
    }

    private readonly record struct FrameworkSandboxType(
        Func<Type, bool> Matches,
        Func<Type, int, SandboxType> Create);
}
