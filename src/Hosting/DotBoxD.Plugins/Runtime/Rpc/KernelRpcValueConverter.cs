namespace DotBoxD.Plugins;

using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// Converts between the compact server extension wire IR and the sandbox values consumed by installed verified
/// IR. The expected sandbox type is supplied by the server-side installed function signature.
/// </summary>
public static class KernelRpcValueConverter
{
    private static readonly Dictionary<string, KernelRpcScalarConverter> ScalarConverters = new(StringComparer.Ordinal)
    {
        ["Unit"] = static value =>
        {
            RequireWireKind(value, KernelRpcValueKind.Unit);
            return SandboxValue.Unit;
        },
        ["Bool"] = static value =>
        {
            RequireWireKind(value, KernelRpcValueKind.Bool);
            return SandboxValue.FromBool(value.BoolValue);
        },
        ["I32"] = static value =>
        {
            RequireWireKind(value, KernelRpcValueKind.I32);
            return SandboxValue.FromInt32(value.Int32Value);
        },
        ["I64"] = static value =>
        {
            RequireWireKind(value, KernelRpcValueKind.I64);
            return SandboxValue.FromInt64(value.Int64Value);
        },
        ["F64"] = static value =>
        {
            RequireWireKind(value, KernelRpcValueKind.F64);
            return SandboxValue.FromDouble(value.DoubleValue);
        },
        ["String"] = static value =>
        {
            RequireWireKind(value, KernelRpcValueKind.String);
            return SandboxValue.FromString(value.TextValue);
        },
        ["Guid"] = static value =>
        {
            RequireWireKind(value, KernelRpcValueKind.Guid);
            return SandboxValue.FromGuid(value.GuidValue);
        },
    };

    private delegate SandboxValue KernelRpcScalarConverter(KernelRpcValue value);

    public static KernelRpcValue FromSandboxValue(SandboxValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (TryScalarFromSandboxValue(value, out var scalar))
        {
            return scalar;
        }

        return FromSandboxCollectionValue(value);
    }

    internal static void RequireDeclaredType(SandboxValue value, SandboxType expectedType)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(expectedType);
        if (!SandboxValueTypeMatcher.MatchesExactType(value, expectedType))
        {
            throw new ArgumentException(
                "Server extension IPC cannot marshal a sandbox collection whose declared type does not match its contents.",
                nameof(value));
        }
    }

    private static KernelRpcValue FromValidatedSandboxValue(SandboxValue value)
    {
        if (TryScalarFromSandboxValue(value, out var scalar))
        {
            return scalar;
        }

        return FromSandboxCollectionValue(value);
    }

    private static KernelRpcValue FromSandboxCollectionValue(SandboxValue value)
    {
        return value switch
        {
            ListValue list => KernelRpcValue.ListFromOwnedItems(ConvertList(list)),
            RecordValue record => KernelRpcValue.RecordFromOwnedFields(ConvertList(record.Fields)),
            MapValue map => KernelRpcValue.MapFromOwnedEntries(ConvertMap(map)),
            _ => throw new NotSupportedException(
                $"Server extension IPC cannot marshal sandbox value '{value.GetType().Name}'.")
        };
    }

    private static bool TryScalarFromSandboxValue(SandboxValue value, out KernelRpcValue converted)
    {
        switch (value)
        {
            case UnitValue:
                converted = KernelRpcValue.Unit();
                return true;
            case BoolValue boolean:
                converted = KernelRpcValue.Bool(boolean.Value);
                return true;
            case I32Value number:
                converted = KernelRpcValue.Int32(number.Value);
                return true;
            case I64Value number:
                converted = KernelRpcValue.Int64(number.Value);
                return true;
            case F64Value number:
                converted = KernelRpcValue.Double(number.Value);
                return true;
            case StringValue text:
                converted = KernelRpcValue.String(text.Value);
                return true;
            case GuidValue guid:
                converted = KernelRpcValue.Guid(guid.Value);
                return true;
            default:
                converted = default;
                return false;
        }
    }

    public static SandboxValue ToSandboxValue(KernelRpcValue value, SandboxType expectedType)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        if (TryScalarToSandboxValue(value, expectedType, out var scalar))
        {
            return scalar;
        }

        if (expectedType.Name == "List" && expectedType.Arguments.Count == 1)
        {
            return ListToSandboxValue(value, expectedType.Arguments[0]);
        }

        if (expectedType.Name == "Map" && expectedType.Arguments.Count == 2)
        {
            return MapToSandboxValue(value, expectedType.Arguments[0], expectedType.Arguments[1]);
        }

        if (expectedType.IsRecord)
        {
            return RecordToSandboxValue(value, expectedType);
        }

        throw new NotSupportedException($"Server extension IPC cannot marshal expected sandbox type '{expectedType}'.");
    }

    private static void RequireWireKind(KernelRpcValue value, KernelRpcValueKind expected)
    {
        if (value.Kind != expected)
        {
            throw new FormatException($"Server extension value expected '{expected}' but received '{value.Kind}'.");
        }
    }

    private static bool TryScalarToSandboxValue(KernelRpcValue value, SandboxType expectedType, out SandboxValue result)
    {
        if (expectedType.Arguments.Count == 0 && ScalarConverters.TryGetValue(expectedType.Name, out var convert))
        {
            result = convert(value);
            return true;
        }

        result = null!;
        return false;
    }

    private static SandboxValue ListToSandboxValue(KernelRpcValue value, SandboxType itemType)
    {
        RequireWireKind(value, KernelRpcValueKind.List);
        var source = value.ItemSpan;
        var items = source.Length == 0
            ? Array.Empty<SandboxValue>()
            : new SandboxValue[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            items[i] = ToSandboxValue(source[i], itemType);
        }

        return SandboxValue.FromOwnedList(items, itemType);
    }

    private static SandboxValue MapToSandboxValue(KernelRpcValue value, SandboxType keyType, SandboxType valueType)
    {
        RequireWireKind(value, KernelRpcValueKind.Map);
        var source = value.ItemSpan;
        var entries = new MapValueBuilder(source.Length / 2);
        for (var i = 0; i + 1 < source.Length; i += 2)
        {
            var key = ToSandboxValue(source[i], keyType);
            var item = ToSandboxValue(source[i + 1], valueType);
            if (!entries.TryAdd(key, item))
            {
                throw new FormatException("Server extension IPC map payload contains a duplicate key.");
            }
        }

        return SandboxValue.FromOwnedMap(entries, keyType, valueType);
    }

    private static SandboxValue RecordToSandboxValue(KernelRpcValue value, SandboxType expectedType)
    {
        RequireWireKind(value, KernelRpcValueKind.Record);
        var source = value.ItemSpan;
        if (source.Length != expectedType.Arguments.Count)
        {
            throw new NotSupportedException(
                $"Server extension IPC record expected {expectedType.Arguments.Count} field(s) but received {source.Length}.");
        }

        var fields = source.Length == 0
            ? Array.Empty<SandboxValue>()
            : new SandboxValue[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            fields[i] = ToSandboxValue(source[i], expectedType.Arguments[i]);
        }

        return SandboxValue.FromOwnedRecord(fields);
    }

    private static KernelRpcValue[] ConvertList(IReadOnlyList<SandboxValue> values)
    {
        if (values.Count == 0)
        {
            return Array.Empty<KernelRpcValue>();
        }

        var converted = new KernelRpcValue[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            converted[i] = FromValidatedSandboxValue(values[i]);
        }

        return converted;
    }

    private static KernelRpcValue[] ConvertList(ListValue values)
    {
        if (values.Values.Count == 0)
        {
            return Array.Empty<KernelRpcValue>();
        }

        var converted = new KernelRpcValue[values.Values.Count];
        for (var i = 0; i < values.Values.Count; i++)
        {
            var item = values.Values[i];
            RequireDeclaredType(item, values.ItemType);
            converted[i] = FromValidatedSandboxValue(item);
        }

        return converted;
    }

    // Maps marshal to a flat key/value sequence (key, value, key, value, …) to match
    // KernelRpcValue.Map's representation; the host reads it back into a Dictionary by pairs.
    private static KernelRpcValue[] ConvertMap(MapValue values)
    {
        if (values.Values.Count == 0)
        {
            return Array.Empty<KernelRpcValue>();
        }

        var entries = new KernelRpcValue[values.Values.Count * 2];
        var index = 0;
        foreach (var pair in values.Entries)
        {
            RequireDeclaredType(pair.Key, values.KeyType);
            RequireDeclaredType(pair.Value, values.ValueType);
            entries[index++] = FromValidatedSandboxValue(pair.Key);
            entries[index++] = FromValidatedSandboxValue(pair.Value);
        }

        return entries;
    }
}
