using Microsoft.CodeAnalysis;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;
namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;
/// <summary>
/// Maps C# types used by a <c>[ServerExtension]</c> batch method onto DotBoxD.Kernels JSON IR types: scalars to
/// their sandbox names, <c>List&lt;T&gt;</c>/<c>IEnumerable&lt;T&gt;</c>/<c>T[]</c> to <c>List</c>, and a
/// DTO (record/struct/class of supported fields) to a positional <c>Record</c>. A DTO's fields are its
/// public readable properties followed by public instance fields, which is also the order <c>record.new</c>
/// arguments and <c>record.get</c> indices use. Anything unsupported throws <see cref="NotSupportedException"/> so
/// the whole kernel fails generation safely.
/// </summary>
internal static partial class DotBoxDRpcTypeMapper
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

    public static string JsonType(ITypeSymbol type, Compilation compilation)
        => JsonType(type, compilation, 0, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

    private static string JsonType(
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
        var valueType = JsonType(nullableUnderlying, context.Compilation, context.Depth + 1, context.Visiting);
        return $"{{\"name\":\"Record\",\"arguments\":[\"Bool\",{valueType}]}}";
    }

    private static string? TrySpecialScalarJsonType(JsonTypeContext context)
    {
        switch (context.Type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return Scalar("Bool");
            case SpecialType.System_Int32:
                return Scalar("I32");
            case SpecialType.System_Int64:
                return Scalar("I64");
            case SpecialType.System_Double:
            case SpecialType.System_Single:
                return Scalar("F64");
            case SpecialType.System_Decimal:
                RejectTooDeep(context.Type, context.Depth);
                return DecimalWireJsonType();
            case SpecialType.System_String:
                return Scalar("String");
            default:
                return null;
        }
    }

    private static string? TryFrameworkJsonType(JsonTypeContext context)
    {
        if (IsGuid(context.Type))
            return Scalar("Guid");

        if (IsDateTimeWireType(context.Type))
            return BoundedJsonType(context.Type, context.Depth, DateTimeWireJsonType());

        if (IsDateOnlyWireType(context.Type))
            return Scalar("I32");

        if (IsTimeOnlyWireType(context.Type) || IsTimeSpanWireType(context.Type))
            return Scalar("I64");

        if (IsCancellationTokenWireType(context.Type))
            return Scalar("Bool");

        if (IsIndexWireType(context.Type))
            return BoundedJsonType(context.Type, context.Depth, IndexWireJsonType());

        return IsRangeWireType(context.Type)
            ? BoundedJsonType(context.Type, context.Depth + 1, RangeWireJsonType())
            : null;
    }

    private static string BoundedJsonType(ITypeSymbol type, int depth, string json)
    {
        RejectTooDeep(type, depth);
        return json;
    }

    private static string? TryEnumJsonType(JsonTypeContext context)
        => context.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            ? Scalar(EnumUsesI64(enumType) ? "I64" : "I32")
            : null;

    private static string? TryListJsonType(JsonTypeContext context)
    {
        if (ListElementType(context.Type) is not { } elementType)
            return null;

        RejectTooDeep(context.Type, context.Depth);
        var json = JsonType(elementType, context.Compilation, context.Depth + 1, context.Visiting);
        return $"{{\"name\":\"List\",\"arguments\":[{json}]}}";
    }

    private static string? TryMapJsonType(JsonTypeContext context)
    {
        if (MapTypes(context.Type) is not { } map)
            return null;

        RejectTooDeep(context.Type, context.Depth);
        RejectUnsupportedMapKey(map.Key);
        var key = JsonType(map.Key, context.Compilation, context.Depth + 1, context.Visiting);
        var value = JsonType(map.Value, context.Compilation, context.Depth + 1, context.Visiting);
        return $"{{\"name\":\"Map\",\"arguments\":[{key},{value}]}}";
    }

    private static void RejectUnsupportedMapKey(ITypeSymbol key)
    {
        if (!IsSupportedMapKey(key))
        {
            throw new NotSupportedException(
                $"Server extension map key type '{key.ToDisplayString()}' is not supported; " +
                "map keys must be bool, int, long, string, DateOnly, TimeOnly, TimeSpan, or an enum.");
        }
    }

    private static string? TryRecordDtoJsonType(JsonTypeContext context)
    {
        if (context.Type is not INamedTypeSymbol named || !IsRecordDto(named))
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
        RejectInheritedDtoProperties(named);
        var fields = RecordFields(named);
        var fieldTypes = new List<string>(fields.Count);
        foreach (var field in fields)
        {
            fieldTypes.Add(JsonType(field.Type, context.Compilation, context.Depth + 1, context.Visiting));
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

    /// <summary>The element type of a list-shaped parameter/return (<c>List&lt;T&gt;</c>,
    /// <c>IReadOnlyList&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, or <c>T[]</c>), else null.</summary>
    public static ITypeSymbol? ListElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            if (array.Rank != 1)
            {
                throw new NotSupportedException(
                    $"Server extension multidimensional array type '{array.ToDisplayString()}' is not supported.");
            }

            return array.ElementType;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            var definition = named.ConstructedFrom.ToDisplayString();
            if (definition is TypeNames.ListOriginal
                or TypeNames.ReadOnlyListOriginal
                or TypeNames.ListInterfaceOriginal
                or TypeNames.EnumerableOriginal
                or TypeNames.ReadOnlyCollectionOriginal)
            {
                return named.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>The key and value types of a map-shaped parameter/return/field (<c>Dictionary&lt;K,V&gt;</c>,
    /// <c>IReadOnlyDictionary&lt;K,V&gt;</c>, or <c>IDictionary&lt;K,V&gt;</c>), else null.</summary>
    public static (ITypeSymbol Key, ITypeSymbol Value)? MapTypes(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true } named && named.TypeArguments.Length == 2)
        {
            var definition = named.ConstructedFrom.ToDisplayString();
            if (definition is TypeNames.DictionaryOriginal
                or TypeNames.ReadOnlyDictionaryOriginal
                or TypeNames.DictionaryInterfaceOriginal)
            {
                return (named.TypeArguments[0], named.TypeArguments[1]);
            }
        }

        return null;
    }

    public static bool SupportsIndexedListWrite(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return false;
        }

        var definition = named.ConstructedFrom.ToDisplayString();
        return definition is TypeNames.ListOriginal
            or TypeNames.ReadOnlyListOriginal
            or TypeNames.ListInterfaceOriginal;
    }

    public static bool IsRecordDto(INamedTypeSymbol type)
        => type.TypeKind is TypeKind.Class or TypeKind.Struct &&
           !IsScalar(type) &&
           !IsFirstClassFrameworkWireStruct(type) &&
           !DotBoxDNullableScalarType.IsNullableValueType(type) &&
           ListElementType(type) is null &&
           MapTypes(type) is null &&
           !ImplementsGenericEnumerable(type) &&
           RecordFields(type).Count > 0;

    /// <summary>
    /// The DTO's positional fields: public readable properties first, then public instance fields. That order
    /// mirrors the runtime reflection shape, keeps existing property DTOs stable, and lets a DTO that mixes
    /// properties with fields marshal every public wire member instead of silently dropping fields.
    /// </summary>
    public static IReadOnlyList<RecordMember> RecordFields(INamedTypeSymbol type)
    {
        var members = new List<RecordMember>();
        foreach (var member in type.GetMembers())
        {
            if (member is IPropertySymbol
                {
                    DeclaredAccessibility: Accessibility.Public,
                    IsStatic: false,
                    GetMethod: not null,
                    IsIndexer: false
                } property &&
                property.GetMethod.DeclaredAccessibility == Accessibility.Public &&
                !property.IsImplicitlyDeclared &&
                !IsIgnoredDataMember(property))
            {
                members.Add(new RecordMember(property.Name, property.Type, property));
            }
        }

        foreach (var member in type.GetMembers())
        {
            if (member is IFieldSymbol
                {
                    DeclaredAccessibility: Accessibility.Public,
                    IsStatic: false,
                    IsConst: false
                } field &&
                !field.IsImplicitlyDeclared &&
                !IsIgnoredDataMember(field))
            {
                members.Add(new RecordMember(field.Name, field.Type, field));
            }
        }

        return members;
    }

    /// <summary>True when <paramref name="member"/> is marked <c>[IgnoreDataMember]</c>.
    /// Such a member is non-wire, so the analyzer, convention adapter, and record decoder all exclude it from
    /// the marshalled field set.</summary>
    public static bool IsIgnoredDataMember(ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass &&
                string.Equals(
                    attributeClass.ToDisplayString(),
                    "System.Runtime.Serialization.IgnoreDataMemberAttribute",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Scalar(string name) => "\"" + name + "\"";
}
