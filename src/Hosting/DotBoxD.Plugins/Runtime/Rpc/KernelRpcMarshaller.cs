using System.Collections;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime.Rpc;

/// <summary>
/// Marshals between plain C# values and the sandbox <see cref="SandboxValue"/> world for server extension
/// service calls: caller arguments are converted to sandbox values for
/// <see cref="InstalledKernel.InvokeServerExtensionAsync"/>, and the returned value is converted back to the
/// declared C# result type. Supports the supported scalars, <c>List&lt;T&gt;</c>/<c>IEnumerable&lt;T&gt;</c>,
/// and DTOs (records/structs/classes) mapped to positional records by their fields' declaration order —
/// the same order the analyzer used when it lowered the kernel, so fields line up by position.
/// </summary>
public static partial class KernelRpcMarshaller
{
    public static SandboxValue ToSandboxValue(object? value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (TryNullableToSandboxValue(value, type, out var nullable))
        {
            return nullable;
        }

        ArgumentNullException.ThrowIfNull(value);
        if (TryScalarToSandbox(value, type) is { } scalar)
        {
            return scalar;
        }

        if (TryDateTimeToSandboxValue(value, type, out var dateTime))
        {
            return dateTime;
        }

        if (TryDecimalToSandboxValue(value, type, out var decimalValue))
        {
            return decimalValue;
        }

        if (TryFrameworkStructToSandboxValue(value, type, out var frameworkStruct))
        {
            return frameworkStruct;
        }

        if (TryStructuredToSandboxValue(value, type, out var structured))
        {
            return structured;
        }

        throw new NotSupportedException($"Server extension cannot marshal type '{type}' to a sandbox value.");
    }

    private static bool TryStructuredToSandboxValue(object value, Type type, out SandboxValue result)
    {
        if (type.IsEnum)
        {
            result = EnumUsesI64(type)
                ? SandboxValue.FromInt64(EnumToInt64(value, type))
                : SandboxValue.FromInt32(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        if (ElementType(type) is { } elementType)
        {
            result = EnumerableToSandboxValue(value, type, elementType);
            return true;
        }

        if (MapTypes(type) is { } mapTypes)
        {
            result = DictionaryToSandboxValue(value, type, mapTypes);
            return true;
        }

        if (DtoShape(type) is { } shape)
        {
            result = DtoToSandboxValue(value, shape);
            return true;
        }

        result = null!;
        return false;
    }

    private static SandboxValue EnumerableToSandboxValue(object value, Type type, Type elementType)
    {
        if (value is not IEnumerable enumerable)
        {
            throw new ArgumentException(
                $"Kernel RPC service expected '{type}' to be enumerable.",
                nameof(value));
        }

        return value is ICollection collection
            ? CollectionToSandboxValue(collection, enumerable, elementType)
            : StreamingEnumerableToSandboxValue(enumerable, elementType);
    }

    private static SandboxValue CollectionToSandboxValue(
        ICollection collection,
        IEnumerable enumerable,
        Type elementType)
    {
        var itemType = SandboxTypeOf(elementType);
        var items = collection.Count == 0
            ? Array.Empty<SandboxValue>()
            : new SandboxValue[collection.Count];
        var index = 0;
        foreach (var item in enumerable)
        {
            items[index++] = MarshalChild(item, elementType, "List element");
        }

        return SandboxValue.FromOwnedList(items, itemType);
    }

    private static SandboxValue StreamingEnumerableToSandboxValue(IEnumerable enumerable, Type elementType)
    {
        var values = new List<SandboxValue>();
        foreach (var item in enumerable)
        {
            values.Add(MarshalChild(item, elementType, "List element"));
        }

        return SandboxValue.FromOwnedList(values.ToArray(), SandboxTypeOf(elementType));
    }

    private static SandboxValue DictionaryToSandboxValue(
        object value,
        Type type,
        (Type Key, Type Value) mapTypes)
    {
        if (value is not IEnumerable enumerable)
        {
            throw new ArgumentException(
                $"Kernel RPC service expected '{type}' to be a dictionary.",
                nameof(value));
        }

        var keyType = SupportedMapKeySandboxType(mapTypes.Key);
        var valueType = SandboxTypeOf(mapTypes.Value);
        var entries = new MapValueBuilder(value is ICollection collection ? collection.Count : 0);
        foreach (var entry in MapEntries(enumerable, mapTypes.Key, mapTypes.Value))
        {
            var key = MarshalChild(entry.Key, mapTypes.Key, "Map key");
            entries.Set(key, MarshalChild(entry.Value, mapTypes.Value, "Map value"));
        }

        return SandboxValue.FromOwnedMap(entries, keyType, valueType);
    }

    private static SandboxType SupportedMapKeySandboxType(Type key)
    {
        RejectUnsupportedMapKeyType(key);
        var keyType = SandboxTypeOf(key);
        // Mirror the SandboxTypeOf map-key guard: the kernel verifier only accepts a fixed set of scalar map
        // keys (bool/int/long/string/opaque-id, not Guid or double). Reject an unsupported key here with a
        // catchable NotSupportedException instead of producing a Map<Guid,V> that later fails IsKnown at install.
        if (!keyType.IsValidMapKey())
        {
            throw new NotSupportedException(
                $"Kernel RPC service map key type '{key}' is not a supported sandbox map key.");
        }

        return keyType;
    }

    private static SandboxValue DtoToSandboxValue(object value, RecordShape shape)
    {
        shape.RejectUnmatchedRequiredConstructor();
        var fields = shape.Fields;
        var fieldValues = shape.GetValues(value);
        shape.RejectUnreconstructibleOutboundValue(fieldValues);
        var values = new SandboxValue[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            values[i] = MarshalChild(fieldValues[i], fields[i].Type, $"DTO field '{fields[i].Name}'");
        }

        return SandboxValue.FromOwnedRecord(values);
    }

    // A nested child (list element, map key/value, or DTO field) of a marshaller-eligible value. The sandbox
    // value model has no null, so a null child is rejected with a clear, contextual NotSupportedException rather
    // than the bare ArgumentNullException ToSandboxValue would otherwise throw with only the parameter name.
    private static SandboxValue MarshalChild(object? value, Type type, string context)
    {
        if (value is null && Nullable.GetUnderlyingType(type) is null)
        {
            throw new NotSupportedException(
                $"{context} of type '{type}' was null; the sandbox value model has no null.");
        }

        return ToSandboxValue(value, type);
    }

    public static object? FromSandboxValue(SandboxValue value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);
        if (TryCoreFromSandboxValue(value, type, out var core))
        {
            return core;
        }

        return FromStructuredSandboxValue(value, type);
    }

    private static bool TryCoreFromSandboxValue(SandboxValue value, Type type, out object? result)
    {
        result = null;
        if (TryNullableFromSandboxValue(value, type, out var nullable))
        {
            result = nullable;
            return true;
        }

        if (TryScalarFromSandbox(value, type, out var scalar))
        {
            result = scalar;
            return true;
        }

        if (TryDateTimeFromSandboxValue(value, type, out var dateTime))
        {
            result = dateTime;
            return true;
        }

        if (TryDecimalFromSandboxValue(value, type, out var decimalValue))
        {
            result = decimalValue;
            return true;
        }

        if (TryFrameworkStructFromSandboxValue(value, type, out var frameworkStruct))
        {
            result = frameworkStruct;
            return true;
        }

        return false;
    }

    private static object? FromStructuredSandboxValue(SandboxValue value, Type type)
    {
        if (type.IsEnum)
        {
            return EnumFromSandboxValue(value, type);
        }

        if (value is RecordValue record && DtoShape(type) is { } shape)
        {
            return DtoFromSandboxValue(record, type, shape);
        }

        if (ElementType(type) is { } elementType && value is ListValue list)
        {
            return ListFromSandboxValue(list, type, elementType);
        }

        if (MapTypes(type) is { } mapTypes && value is MapValue map)
        {
            return MapFromSandboxValue(map, type, mapTypes);
        }

        throw new NotSupportedException($"Server extension cannot marshal a sandbox value to type '{type}'.");
    }

    private static object EnumFromSandboxValue(SandboxValue value, Type type)
    {
        if (EnumUsesI64(type))
        {
            return value is I64Value longValue
                ? EnumFromInt64(type, longValue.Value)
                : throw CannotMarshalEnum(value, type, SandboxType.I64);
        }

        return value is I32Value intValue
            ? EnumFromInt32(type, intValue.Value)
            : throw CannotMarshalEnum(value, type, SandboxType.I32);
    }

    private static object DtoFromSandboxValue(RecordValue record, Type type, RecordShape shape)
    {
        var fields = shape.Fields;
        if (record.Fields.Count != fields.Count)
        {
            throw new NotSupportedException($"Server extension record has {record.Fields.Count} fields but '{type}' expects {fields.Count}.");
        }

        return shape.Construct(record);
    }

    private static object ListFromSandboxValue(ListValue list, Type type, Type elementType)
    {
        if (type.IsArray)
        {
            return ToArray(list.Values, elementType);
        }

        var resultList = CreateList(elementType, list.Values.Count);
        foreach (var item in list.Values)
        {
            resultList.Add(FromSandboxValue(item, elementType));
        }

        return CompleteList(type, elementType, resultList);
    }

    private static object MapFromSandboxValue(MapValue map, Type type, (Type Key, Type Value) mapTypes)
    {
        RejectUnsupportedMapKeyType(mapTypes.Key);
        var result = CreateDictionary(mapTypes.Key, mapTypes.Value, map.Values.Count);
        foreach (var pair in map.Entries)
        {
            var key = FromSandboxValue(pair.Key, mapTypes.Key)
                ?? throw new NotSupportedException("Server extension cannot marshal a null map key.");
            result[key] = FromSandboxValue(pair.Value, mapTypes.Value);
        }

        return CompleteDictionary(type, mapTypes.Key, mapTypes.Value, result);
    }

    private static NotSupportedException CannotMarshalEnum(
        SandboxValue value,
        Type type,
        SandboxType expected)
        => new($"Server extension cannot marshal sandbox value '{value.Type}' to enum '{type}'; expected '{expected}'.");
}
