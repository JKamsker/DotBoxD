using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static readonly ConditionalWeakTable<Type, Func<IList, object>> ReadOnlyListWrapperFactoryCache = new();
    private static readonly ConditionalWeakTable<Type, Func<IDictionary, object>> ReadOnlyDictionaryWrapperFactoryCache = new();

    private static object CompleteList(Type targetType, Type elementType, IList list)
        => IsReadOnlyListTarget(targetType)
            ? ReadOnlyListWrapperFactoryCache.GetValue(elementType, CreateReadOnlyListWrapperFactory)(list)
            : list;

    private static object CompleteDictionary(Type targetType, IDictionary dictionary)
        => IsReadOnlyDictionaryTarget(targetType)
            ? ReadOnlyDictionaryWrapperFactoryCache.GetValue(targetType, CreateReadOnlyDictionaryWrapperFactory)(dictionary)
            : dictionary;

    private static bool IsReadOnlyListTarget(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IReadOnlyCollection<>) ||
            definition == typeof(IReadOnlyList<>) ||
            definition == typeof(IEnumerable<>);
    }

    private static bool IsReadOnlyDictionaryTarget(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>);

    private static Func<IList, object> CreateReadOnlyListWrapperFactory(Type elementType)
    {
        var constructor = typeof(ReadOnlyCollection<>)
            .MakeGenericType(elementType)
            .GetConstructor([typeof(IList<>).MakeGenericType(elementType)])
            ?? throw MissingReadOnlyConstructor("ReadOnlyCollection", elementType);

        var source = LinqExpression.Parameter(typeof(IList), "source");
        var created = LinqExpression.New(
            constructor,
            LinqExpression.Convert(source, typeof(IList<>).MakeGenericType(elementType)));
        return LinqExpression.Lambda<Func<IList, object>>(
            LinqExpression.Convert(created, typeof(object)),
            source).Compile();
    }

    private static Func<IDictionary, object> CreateReadOnlyDictionaryWrapperFactory(Type targetType)
    {
        var (keyType, valueType) = MapTypes(targetType)!.Value;
        var constructor = typeof(ReadOnlyDictionary<,>)
            .MakeGenericType(keyType, valueType)
            .GetConstructor([typeof(IDictionary<,>).MakeGenericType(keyType, valueType)])
            ?? throw MissingReadOnlyConstructor("ReadOnlyDictionary", keyType, valueType);

        var source = LinqExpression.Parameter(typeof(IDictionary), "source");
        var created = LinqExpression.New(
            constructor,
            LinqExpression.Convert(source, typeof(IDictionary<,>).MakeGenericType(keyType, valueType)));
        return LinqExpression.Lambda<Func<IDictionary, object>>(
            LinqExpression.Convert(created, typeof(object)),
            source).Compile();
    }

    private static MissingMethodException MissingReadOnlyConstructor(string typeName, Type first, Type? second = null)
        => second is null
            ? new MissingMethodException($"{typeName}<{first}>", ".ctor")
            : new MissingMethodException($"{typeName}<{first},{second}>", ".ctor");
}
