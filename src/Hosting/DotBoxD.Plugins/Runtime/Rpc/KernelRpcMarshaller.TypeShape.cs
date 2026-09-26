using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    // Cache lifetimes follow the model types, including both generic arguments of a dictionary target.
    private static readonly ConditionalWeakTable<Type, OptionalType> ElementTypeCache = new();
    private static readonly ConditionalWeakTable<Type, OptionalMapTypes> MapTypeCache = new();
    private static readonly ConditionalWeakTable<Type, Func<int, IList>> ListFactoryCache = new();
    private static readonly ConditionalWeakTable<Type, Func<int, IDictionary>> DictionaryFactoryCache = new();
    private static readonly ConditionalWeakTable<Type, RecordShape> RecordShapeCache = new();
    private static readonly ConditionalWeakTable<Type, OptionalRecordShape> DtoShapeCache = new();

    private static readonly HashSet<Type> NonDtoShapeTypes =
    [
        typeof(TimeSpan),
        typeof(string)
    ];

    private static Type? ElementType(Type type)
        => ElementTypeCache.GetValue(type, static candidate => new OptionalType(FindElementType(candidate))).Value;

    private static (Type Key, Type Value)? MapTypes(Type type)
        => MapTypeCache.GetValue(type, static candidate => new OptionalMapTypes(FindMapTypes(candidate))).Value;

    private static (Type Key, Type Value)? FindMapTypes(Type type)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Dictionary<,>) ||
                definition == typeof(IReadOnlyDictionary<,>) ||
                definition == typeof(IDictionary<,>))
            {
                var arguments = type.GetGenericArguments();
                return (arguments[0], arguments[1]);
            }
        }

        return null;
    }

    private static Type? FindElementType(Type type)
    {
        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1)
            {
                throw new NotSupportedException(
                    $"Kernel RPC service cannot marshal multidimensional array type '{type}'.");
            }

            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IList<>) || definition == typeof(IEnumerable<>) ||
                definition == typeof(IReadOnlyCollection<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static RecordShape? DtoShape(Type type)
        => DtoShapeCache.GetValue(type, static candidate => new OptionalRecordShape(FindDtoShape(candidate))).Value;

    private static RecordShape? FindDtoShape(Type type)
    {
        if (IsNonDtoShape(type))
        {
            return null;
        }

        var shape = GetRecordShape(type);
        return shape.Fields.Count > 0 ? shape : null;
    }

    private static bool IsNonDtoShape(Type type)
        => IsKnownNonDtoShape(type) ||
           IsCollectionShape(type) ||
           !IsDtoContainerShape(type) ||
           ImplementsGenericEnumerable(type);

    private static bool IsKnownNonDtoShape(Type type)
        => NonDtoShapeTypes.Contains(type) ||
           IsDateTimeWireType(type) ||
           IsDecimalWireType(type) ||
           IsFrameworkStructWireType(type) ||
           type.IsPrimitive ||
           type.IsEnum;

    private static bool IsCollectionShape(Type type)
        => ElementType(type) is not null || MapTypes(type) is not null;

    private static bool IsDtoContainerShape(Type type)
        => type.IsClass || type.IsValueType;

    private static IList CreateList(Type elementType, int capacity)
        => ListFactoryCache.GetValue(elementType, CreateListFactory)(capacity);

    private static IDictionary CreateDictionary(Type targetType, int capacity)
        => DictionaryFactoryCache.GetValue(targetType, CreateDictionaryFactory)(capacity);

    // An IEnumerable<T> reaches here only after the recognized list/map shapes have been ruled out, so any
    // remaining one — e.g. ImmutableArray<T>, ImmutableList<T>, Queue<T> — exposes only scalar getters
    // (Length/Count/...) and would otherwise be mis-marshalled as a metadata-only record that silently drops its
    // elements. Excluding it makes the type fail closed with the marshaller's unsupported-type exception, mirroring
    // the analyzer's DotBoxDRpcTypeMapper.ImplementsGenericEnumerable. A plain DTO does not implement
    // IEnumerable<T>, so this does not over-exclude.
    private static bool ImplementsGenericEnumerable(Type type)
    {
        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType &&
                @interface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return true;
            }
        }

        return false;
    }

    private static Func<int, IList> CreateListFactory(Type elementType)
    {
        var constructor = typeof(List<>)
            .MakeGenericType(elementType)
            .GetConstructor([typeof(int)])
            ?? throw new MissingMethodException($"List<{elementType}>", ".ctor(int)");
        return CompileCollectionFactory<IList>(constructor);
    }

    private static Func<int, IDictionary> CreateDictionaryFactory(Type targetType)
    {
        var types = MapTypes(targetType)!.Value;
        var constructor = typeof(Dictionary<,>)
            .MakeGenericType(types.Key, types.Value)
            .GetConstructor([typeof(int)])
            ?? throw new MissingMethodException($"Dictionary<{types.Key},{types.Value}>", ".ctor(int)");
        return CompileCollectionFactory<IDictionary>(constructor);
    }

    private static Func<int, TCollection> CompileCollectionFactory<TCollection>(ConstructorInfo constructor)
    {
        var capacity = LinqExpression.Parameter(typeof(int), "capacity");
        var created = LinqExpression.New(constructor, capacity);
        return LinqExpression.Lambda<Func<int, TCollection>>(
            LinqExpression.Convert(created, typeof(TCollection)),
            capacity).Compile();
    }

    private static RecordShape GetRecordShape(Type type)
        => RecordShapeCache.GetValue(type, static candidate =>
        {
            var discovered = RecordMemberDiscovery.Discover(candidate);
            var members = new RecordMember[discovered.Count];
            for (var i = 0; i < discovered.Count; i++)
            {
                members[i] = discovered[i] switch
                {
                    PropertyInfo property => RecordMember.FromProperty(property),
                    FieldInfo field => RecordMember.FromField(field),
                    _ => throw new NotSupportedException(
                        $"Unsupported record member '{discovered[i].Name}'."),
                };
            }

            return new RecordShape(candidate, members);
        });

    // A member marked with a known serializer ignore attribute is non-wire: lazily-resolved or computed
    // state, not serialized data. Exclude it from the marshalled record shape so the analyzer, convention
    // event adapter, and record decoder agree on the wire field set. Custom attribute metadata avoids
    // instantiating attributes while retaining their declaring assembly identity.
    internal static bool IsIgnoredMember(MemberInfo member)
    {
        foreach (var attribute in member.GetCustomAttributesData())
        {
            if (IsIgnoreAttribute(attribute.AttributeType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIgnoreAttribute(Type attributeType)
    {
        if (attributeType == typeof(System.Runtime.Serialization.IgnoreDataMemberAttribute) ||
            attributeType == typeof(System.Text.Json.Serialization.JsonIgnoreAttribute))
        {
            return true;
        }

        return attributeType.FullName == "MessagePack.IgnoreMemberAttribute" &&
               attributeType.Assembly.GetName().Name is "MessagePack" or "MessagePack.Annotations" &&
               HasMessagePackPublicKeyToken(attributeType.Assembly.GetName().GetPublicKeyToken());
    }

    private static bool HasMessagePackPublicKeyToken(byte[]? token) =>
        token is { Length: 8 } &&
        token[0] == 0xb4 && token[1] == 0xa0 && token[2] == 0x36 && token[3] == 0x95 &&
        token[4] == 0x45 && token[5] == 0xf0 && token[6] == 0xa1 && token[7] == 0xbe;

    private sealed record OptionalType(Type? Value);

    private sealed record OptionalMapTypes((Type Key, Type Value)? Value);

    private sealed record OptionalRecordShape(RecordShape? Value);
}
