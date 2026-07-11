using System.Collections;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static readonly Dictionary<Type, KernelScalarFromValueConverter> KernelScalarFromValueConverters = new()
    {
        [typeof(bool)] = static value => value.BoolValue,
        [typeof(int)] = static value => value.Int32Value,
        [typeof(long)] = static value => value.Int64Value,
        [typeof(float)] = static value => DoubleToSingle(value.DoubleValue),
        [typeof(double)] = static value => value.DoubleValue,
        [typeof(string)] = static value => value.TextValue,
        [typeof(Guid)] = static value => value.GuidValue,
        [typeof(DateOnly)] = static value => DateOnlyFromDayNumber(value.Int32Value),
        [typeof(TimeOnly)] = static value => TimeOnlyFromTicks(value.Int64Value),
        [typeof(TimeSpan)] = static value => new TimeSpan(value.Int64Value),
        [typeof(CancellationToken)] = static value => new CancellationToken(value.BoolValue),
    };

    private delegate object KernelScalarFromValueConverter(KernelRpcValue value);

    internal static object? FromKernelRpcValue(KernelRpcValue value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (TryNullableFromKernelRpcValue(value, type, out var nullable))
        {
            return nullable;
        }

        if (TryWellKnownFromKernelRpcValue(value, type, out var wellKnown))
        {
            return wellKnown;
        }

        if (TryEnumFromKernelRpcValue(value, type, out var enumValue))
        {
            return enumValue;
        }

        if (TryStructuredFromKernelRpcValue(value, type, out var structured))
        {
            return structured;
        }

        throw new NotSupportedException($"Server extension cannot marshal a kernel RPC value to type '{type}'.");
    }

    private static bool TryWellKnownFromKernelRpcValue(KernelRpcValue value, Type type, out object? result)
    {
        if (TryScalarFromKernel(value, type, out var scalar))
        {
            result = scalar;
            return true;
        }

        if (TryDateTimeFromKernelRpcValue(value, type, out var dateTime))
        {
            result = dateTime;
            return true;
        }

        if (TryDecimalFromKernelRpcValue(value, type, out var decimalValue))
        {
            result = decimalValue;
            return true;
        }

        if (TryFrameworkStructFromKernelRpcValue(value, type, out var frameworkStruct))
        {
            result = frameworkStruct;
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryEnumFromKernelRpcValue(KernelRpcValue value, Type type, out object? result)
    {
        if (!type.IsEnum)
        {
            result = null;
            return false;
        }

        result = EnumUsesI64(type)
            ? EnumFromInt64(type, value.Int64Value)
            : EnumFromInt32(type, value.Int32Value);
        return true;
    }

    private static bool TryStructuredFromKernelRpcValue(KernelRpcValue value, Type type, out object? result)
    {
        if (value.Kind == KernelRpcValueKind.Record && DtoShape(type) is { } shape)
        {
            result = DtoFromKernelRpcValue(value, type, shape);
            return true;
        }

        if (ElementType(type) is { } elementType)
        {
            result = ListFromKernelRpcValue(value, type, elementType);
            return true;
        }

        if (MapTypes(type) is { } mapTypes)
        {
            result = MapFromKernelRpcValue(value, type, mapTypes);
            return true;
        }

        result = null;
        return false;
    }

    private static object DtoFromKernelRpcValue(KernelRpcValue value, Type type, RecordShape shape)
    {
        if (value.ItemCount != shape.Fields.Count)
        {
            throw new NotSupportedException(
                $"Server extension record has {value.ItemCount} fields but '{type}' expects {shape.Fields.Count}.");
        }

        return shape.Construct(value);
    }

    private static object ListFromKernelRpcValue(KernelRpcValue value, Type type, Type elementType)
    {
        value.RequireKind(KernelRpcValueKind.List);
        return type.IsArray
            ? ToArray(value.ItemSpan, elementType)
            : CompleteList(type, elementType, ToList(value.ItemSpan, elementType));
    }

    private static object MapFromKernelRpcValue(
        KernelRpcValue value,
        Type type,
        (Type Key, Type Value) mapTypes)
    {
        value.RequireKind(KernelRpcValueKind.Map);
        RejectUnsupportedMapKeyType(mapTypes.Key);
        return CompleteDictionary(
            type,
            mapTypes.Key,
            mapTypes.Value,
            ToDictionary(value.ItemSpan, mapTypes.Key, mapTypes.Value));
    }

    private static bool TryScalarFromKernel(KernelRpcValue value, Type type, out object? result)
    {
        if (KernelScalarFromValueConverters.TryGetValue(type, out var convert))
        {
            result = convert(value);
            return true;
        }

        result = null;
        return false;
    }

    private static Array ToArray(ReadOnlySpan<KernelRpcValue> values, Type elementType)
    {
        var array = Array.CreateInstance(elementType, values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            array.SetValue(FromKernelRpcValue(values[i], elementType), i);
        }

        return array;
    }

    private static IList ToList(ReadOnlySpan<KernelRpcValue> values, Type elementType)
    {
        var result = CreateList(elementType, values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            result.Add(FromKernelRpcValue(values[i], elementType));
        }

        return result;
    }

    private static IDictionary ToDictionary(ReadOnlySpan<KernelRpcValue> values, Type keyType, Type valueType)
    {
        if ((values.Length & 1) != 0)
        {
            throw new FormatException("Server extension map payload has an odd key/value entry count.");
        }

        var result = CreateDictionary(keyType, valueType, values.Length / 2);
        for (var i = 0; i < values.Length; i += 2)
        {
            var key = FromKernelRpcValue(values[i], keyType)
                ?? throw new NotSupportedException("Server extension cannot marshal a null map key.");
            if (result.Contains(key))
            {
                throw new FormatException("Server extension map payload contains a duplicate key.");
            }

            result.Add(key, FromKernelRpcValue(values[i + 1], valueType));
        }

        return result;
    }
}
