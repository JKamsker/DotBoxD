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
    private const string IgnoreDataMemberAttribute =
        "System.Runtime.Serialization.IgnoreDataMemberAttribute";
    private const string JsonIgnoreAttribute =
        "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string MessagePackIgnoreMemberAttribute =
        "MessagePack.IgnoreMemberAttribute";

    public static string JsonType(ITypeSymbol type, Compilation compilation)
        => RpcJsonTypeResolver.Resolve(type, compilation);

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
        var hierarchy = DtoHierarchy(type);
        var members = new List<RecordMember>();
        AddRecordProperties(type, hierarchy, members);
        AddRecordFields(type, hierarchy, members);
        return members;
    }

    private static void AddRecordProperties(
        INamedTypeSymbol type,
        IReadOnlyList<INamedTypeSymbol> hierarchy,
        List<RecordMember> members)
    {
        foreach (var declaringType in hierarchy)
        {
            foreach (var member in declaringType.GetMembers())
            {
                if (member is IPropertySymbol property)
                {
                    AddRecordProperty(type, members, property);
                }
            }
        }
    }

    private static void AddRecordProperty(
        INamedTypeSymbol type,
        List<RecordMember> members,
        IPropertySymbol property)
    {
        if (!IsReadableRecordProperty(property))
        {
            return;
        }

        var overriddenIndex = OverriddenPropertyIndex(members, property);
        if (IsIgnoredDataMember(property))
        {
            if (overriddenIndex >= 0)
            {
                members.RemoveAt(overriddenIndex);
            }

            return;
        }

        var recordMember = new RecordMember(property.Name, property.Type, property);
        if (overriddenIndex >= 0)
        {
            members[overriddenIndex] = recordMember;
            return;
        }

        RejectDuplicateRecordMember(type, members, property.Name);
        members.Add(recordMember);
    }

    private static bool IsReadableRecordProperty(IPropertySymbol property)
        => property is
        {
            DeclaredAccessibility: Accessibility.Public,
            IsStatic: false,
            GetMethod: { DeclaredAccessibility: Accessibility.Public },
            IsIndexer: false,
            IsImplicitlyDeclared: false
        };

    private static void AddRecordFields(
        INamedTypeSymbol type,
        IReadOnlyList<INamedTypeSymbol> hierarchy,
        List<RecordMember> members)
    {
        foreach (var declaringType in hierarchy)
        {
            foreach (var member in declaringType.GetMembers())
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
                    RejectDuplicateRecordMember(type, members, field.Name);
                    members.Add(new RecordMember(field.Name, field.Type, field));
                }
            }
        }
    }

    /// <summary>True when <paramref name="member"/> is marked with a known serializer ignore attribute.
    /// Such a member is non-wire, so the analyzer, convention adapter, and record decoder all exclude it from
    /// the marshalled field set.</summary>
    public static bool IsIgnoredDataMember(ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (IsIgnoreAttribute(attribute.AttributeClass?.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIgnoreAttribute(string? typeName) =>
        typeName is IgnoreDataMemberAttribute
            or JsonIgnoreAttribute
            or MessagePackIgnoreMemberAttribute;
}
