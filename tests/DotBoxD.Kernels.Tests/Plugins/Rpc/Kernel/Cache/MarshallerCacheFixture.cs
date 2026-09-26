using System.Collections;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

internal static class MarshallerCacheFixture
{
    internal static (object Value, Type DeclaredType) Value(string shape, Type type)
    {
        if (shape.Contains("map", StringComparison.Ordinal))
        {
            return Map(shape, type);
        }

        var state = (MarshallerCacheState)Activator.CreateInstance(type)!;
        state.Value = 42;
        if (shape == "dto")
        {
            return (state, type);
        }

        if (shape == "array")
        {
            var array = Array.CreateInstance(type, 1);
            array.SetValue(state, 0);
            return (array, array.GetType());
        }

        var listType = typeof(List<>).MakeGenericType(type);
        var list = (IList)Activator.CreateInstance(listType)!;
        list.Add(state);
        return (list, shape == "readonly-list" ? typeof(IReadOnlyList<>).MakeGenericType(type) : listType);
    }

    private static (object Value, Type DeclaredType) Map(string shape, Type type)
    {
        var enumKey = shape.Contains("enum", StringComparison.Ordinal);
        var keyType = enumKey ? type : typeof(string);
        var valueType = enumKey ? typeof(int) : type;
        var mapType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        var map = (IDictionary)Activator.CreateInstance(mapType)!;
        if (enumKey)
        {
            map.Add(Enum.ToObject(type, 1), 42);
        }
        else
        {
            var state = (MarshallerCacheState)Activator.CreateInstance(type)!;
            state.Value = 42;
            map.Add("item", state);
        }

        return (map, shape.StartsWith("readonly", StringComparison.Ordinal)
            ? typeof(IReadOnlyDictionary<,>).MakeGenericType(keyType, valueType)
            : mapType);
    }
}

public class MarshallerCacheState
{
    public int Value { get; set; }
}
