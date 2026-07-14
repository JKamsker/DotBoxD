using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static readonly ConcurrentDictionary<Type, OptionalType> ElementTypeCache = new();
    private static readonly ConcurrentDictionary<Type, OptionalMapTypes> MapTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Func<int, IList>> ListFactoryCache = new();
    private static readonly ConcurrentDictionary<(Type Key, Type Value), Func<int, IDictionary>> DictionaryFactoryCache = new();
    private static readonly ConcurrentDictionary<Type, RecordShape> RecordShapeCache = new();
    private static readonly ConcurrentDictionary<Type, OptionalRecordShape> DtoShapeCache = new();

    private static readonly HashSet<Type> NonDtoShapeTypes =
    [
        typeof(TimeSpan),
        typeof(string)
    ];

    private static Type? ElementType(Type type)
        => ElementTypeCache.GetOrAdd(type, static candidate => new OptionalType(FindElementType(candidate))).Value;

    private static (Type Key, Type Value)? MapTypes(Type type)
        => MapTypeCache.GetOrAdd(type, static candidate => new OptionalMapTypes(FindMapTypes(candidate))).Value;

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
        => DtoShapeCache.GetOrAdd(type, static candidate => new OptionalRecordShape(FindDtoShape(candidate))).Value;

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
        => ListFactoryCache.GetOrAdd(elementType, CreateListFactory)(capacity);

    private static IDictionary CreateDictionary(Type keyType, Type valueType, int capacity)
        => DictionaryFactoryCache.GetOrAdd((keyType, valueType), CreateDictionaryFactory)(capacity);

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

    private static Func<int, IDictionary> CreateDictionaryFactory((Type Key, Type Value) types)
    {
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
        => RecordShapeCache.GetOrAdd(type, static candidate =>
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
    // event adapter, and record decoder agree on the wire field set. Matched by name via
    // GetCustomAttributesData so the attribute need not load.
    internal static bool IsIgnoredMember(MemberInfo member)
    {
        foreach (var attribute in member.GetCustomAttributesData())
        {
            if (IsIgnoreAttribute(attribute.AttributeType.FullName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIgnoreAttribute(string? typeName) =>
        typeName is "System.Runtime.Serialization.IgnoreDataMemberAttribute"
            or "System.Text.Json.Serialization.JsonIgnoreAttribute"
            or "MessagePack.IgnoreMemberAttribute";

    private readonly record struct OptionalType(Type? Value);

    private readonly record struct OptionalMapTypes((Type Key, Type Value)? Value);

    private readonly record struct OptionalRecordShape(RecordShape? Value);
}
