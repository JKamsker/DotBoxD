using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcJsonTypeResolver
{
    private const int MaxJsonTypeDepth = 8;
    private static readonly JsonTypeResolver[] JsonTypeResolvers =
    [
        TryNullableScalarJsonType,
        TrySpecialScalarJsonType,
        TryFrameworkJsonType,
        TryEnumJsonType,
        TryListJsonType,
        TryMapJsonType,
        TryRecordDtoJsonType,
    ];

    private delegate string? JsonTypeResolver(JsonTypeContext context);

    public static string Resolve(ITypeSymbol type, Compilation compilation)
        => Resolve(type, compilation, 0, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

    private static string Resolve(
        ITypeSymbol type,
        Compilation compilation,
        int depth,
        HashSet<ITypeSymbol> visiting)
    {
        RejectTopLevelOnlyTypes(type, compilation);
        RejectNullableReferenceType(type);
        var context = new JsonTypeContext(type, compilation, depth, visiting);
        foreach (var resolver in JsonTypeResolvers)
        {
            if (resolver(context) is { } json)
            {
                return json;
            }
        }

        throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
    }

    private readonly record struct JsonTypeContext(
        ITypeSymbol Type,
        Compilation Compilation,
        int Depth,
        HashSet<ITypeSymbol> Visiting);

    private static void RejectTopLevelOnlyTypes(ITypeSymbol type, Compilation compilation)
    {
        if (type.SpecialType == SpecialType.System_Void || DotBoxDRpcReturnType.IsTaskLike(type, compilation))
        {
            throw new NotSupportedException(
                $"Server extension type '{type.ToDisplayString()}' is only supported as a top-level return type.");
        }
    }

    private static void RejectNullableReferenceType(ITypeSymbol type)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.IsReferenceType)
        {
            throw new NotSupportedException(
                $"Server extension nullable reference type '{type.ToDisplayString()}' is not supported; " +
                "kernel RPC does not encode null reference values.");
        }
    }

    private static string? TryNullableScalarJsonType(JsonTypeContext context)
    {
        if (!DotBoxDNullableScalarType.IsNullableValueType(context.Type))
        {
            return null;
        }

        if (!DotBoxDNullableScalarType.TryGetSupportedUnderlying(context.Type, out var nullableUnderlying))
        {
            throw new NotSupportedException($"Server extension nullable type '{context.Type.ToDisplayString()}' is not supported.");
        }

        RejectTooDeep(context.Type, context.Depth);
        var valueType = Resolve(nullableUnderlying, context.Compilation, context.Depth + 1, context.Visiting);
        return $"{{\"name\":\"Record\",\"arguments\":[\"Bool\",{valueType}]}}";
    }

    private static string? TrySpecialScalarJsonType(JsonTypeContext context)
        => context.Type.SpecialType switch
        {
            SpecialType.System_Boolean => Scalar("Bool"),
            SpecialType.System_Int32 => Scalar("I32"),
            SpecialType.System_Int64 => Scalar("I64"),
            SpecialType.System_Double or SpecialType.System_Single => Scalar("F64"),
            SpecialType.System_Decimal => BoundedJsonType(
                context.Type,
                context.Depth,
                DotBoxDRpcTypeMapper.DecimalWireJsonType()),
            SpecialType.System_String => Scalar("String"),
            _ => null
        };

    private static string? TryFrameworkJsonType(JsonTypeContext context)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(context.Type))
            return Scalar("Guid");

        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(context.Type))
            return BoundedJsonType(context.Type, context.Depth, DotBoxDRpcTypeMapper.DateTimeWireJsonType());

        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(context.Type))
            return Scalar("I32");

        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(context.Type) ||
            DotBoxDRpcTypeMapper.IsTimeSpanWireType(context.Type))
            return Scalar("I64");

        if (DotBoxDRpcTypeMapper.IsCancellationTokenWireType(context.Type))
            return Scalar("Bool");

        if (DotBoxDRpcTypeMapper.IsIndexWireType(context.Type))
            return BoundedJsonType(context.Type, context.Depth, DotBoxDRpcTypeMapper.IndexWireJsonType());

        return DotBoxDRpcTypeMapper.IsRangeWireType(context.Type)
            ? BoundedJsonType(context.Type, context.Depth + 1, DotBoxDRpcTypeMapper.RangeWireJsonType())
            : null;
    }

    private static string BoundedJsonType(ITypeSymbol type, int depth, string json)
    {
        RejectTooDeep(type, depth);
        return json;
    }

    private static string? TryEnumJsonType(JsonTypeContext context)
        => context.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            ? Scalar(DotBoxDRpcTypeMapper.EnumUsesI64(enumType) ? "I64" : "I32")
            : null;

    private static string? TryListJsonType(JsonTypeContext context)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(context.Type) is not { } elementType)
            return null;

        RejectTooDeep(context.Type, context.Depth);
        var json = Resolve(elementType, context.Compilation, context.Depth + 1, context.Visiting);
        return $"{{\"name\":\"List\",\"arguments\":[{json}]}}";
    }

    private static string? TryMapJsonType(JsonTypeContext context)
    {
        if (DotBoxDRpcTypeMapper.MapTypes(context.Type) is not { } map)
            return null;

        RejectTooDeep(context.Type, context.Depth);
        RejectUnsupportedMapKey(map.Key);
        var key = Resolve(map.Key, context.Compilation, context.Depth + 1, context.Visiting);
        var value = Resolve(map.Value, context.Compilation, context.Depth + 1, context.Visiting);
        return $"{{\"name\":\"Map\",\"arguments\":[{key},{value}]}}";
    }

    private static void RejectUnsupportedMapKey(ITypeSymbol key)
    {
        if (!DotBoxDRpcTypeMapper.IsSupportedMapKey(key))
        {
            throw new NotSupportedException(
                $"Server extension map key type '{key.ToDisplayString()}' is not supported; " +
                "map keys must be bool, int, long, string, DateOnly, TimeOnly, TimeSpan, or an enum.");
        }
    }

    private static string? TryRecordDtoJsonType(JsonTypeContext context)
    {
        if (context.Type is not INamedTypeSymbol named || !DotBoxDRpcTypeMapper.IsRecordDto(named))
            return null;

        RejectTooDeep(context.Type, context.Depth);
        RejectRecursiveDto(named, context.Visiting);
        return RecordDtoJsonType(named, context);
    }

    private static void RejectRecursiveDto(INamedTypeSymbol named, HashSet<ITypeSymbol> visiting)
    {
        if (!visiting.Add(named))
        {
            throw new NotSupportedException(
                $"Server extension DTO type '{named.ToDisplayString()}' is cyclic; recursive DTO shapes are not supported.");
        }
    }

    private static string RecordDtoJsonType(INamedTypeSymbol named, JsonTypeContext context)
    {
        try
        {
            return RecordDtoJsonTypeCore(named, context);
        }
        finally
        {
            context.Visiting.Remove(named);
        }
    }

    private static string RecordDtoJsonTypeCore(INamedTypeSymbol named, JsonTypeContext context)
    {
        DotBoxDRpcTypeMapper.RejectInheritedDtoProperties(named);
        var fields = DotBoxDRpcTypeMapper.RecordFields(named);
        var fieldTypes = new List<string>(fields.Count);
        foreach (var field in fields)
        {
            fieldTypes.Add(Resolve(field.Type, context.Compilation, context.Depth + 1, context.Visiting));
        }

        return $"{{\"name\":\"Record\",\"arguments\":[{string.Join(",", fieldTypes)}]}}";
    }

    private static void RejectTooDeep(ITypeSymbol type, int depth)
    {
        if (depth >= MaxJsonTypeDepth)
        {
            throw new NotSupportedException(
                $"Server extension type '{type.ToDisplayString()}' exceeds the supported RPC shape depth.");
        }
    }

    private static string Scalar(string name) => "\"" + name + "\"";
}
