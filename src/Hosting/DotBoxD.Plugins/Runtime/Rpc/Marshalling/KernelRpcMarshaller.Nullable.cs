using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static readonly Dictionary<Type, Func<SandboxValue>> NullableZeroValueFactories = new()
    {
        [typeof(bool)] = static () => SandboxValue.FromBool(false),
        [typeof(int)] = static () => SandboxValue.FromInt32(0),
        [typeof(long)] = static () => SandboxValue.FromInt64(0L),
        [typeof(float)] = static () => SandboxValue.FromDouble(0D),
        [typeof(double)] = static () => SandboxValue.FromDouble(0D),
        [typeof(Guid)] = static () => SandboxValue.FromGuid(Guid.Empty),
        [typeof(DateTime)] = NullableDateTimeZeroValue,
        [typeof(DateTimeOffset)] = NullableDateTimeZeroValue,
        [typeof(decimal)] = static () => DecimalToSandboxValue(default),
        [typeof(DateOnly)] = static () => SandboxValue.FromInt32(0),
        [typeof(TimeOnly)] = static () => SandboxValue.FromInt64(0L),
        [typeof(TimeSpan)] = static () => SandboxValue.FromInt64(0L),
        [typeof(CancellationToken)] = static () => SandboxValue.FromBool(false)
    };

    internal static void RejectNullableValueTypesForServerExtension(Type type)
    {
        if (ContainsNullableValueType(type, []))
        {
            throw new NotSupportedException($"Server extension nullable type '{type}' is not supported.");
        }
    }

    internal static void RejectUnsupportedNullableValueTypesForServerExtension(Type type)
    {
        if (UnsupportedNullableValueType(type, []) is { } unsupported)
        {
            throw UnsupportedNullableUnderlying(unsupported);
        }
    }

    private static bool TryNullableToSandboxValue(object? value, Type type, out SandboxValue sandbox)
    {
        if (Nullable.GetUnderlyingType(type) is not { } underlying)
        {
            sandbox = null!;
            return false;
        }

        EnsureSupportedNullableUnderlying(underlying);
        sandbox = SandboxValue.FromOwnedRecord(
        [
            SandboxValue.FromBool(value is not null),
            value is null ? NullableZeroValue(underlying) : ToSandboxValue(value, underlying)
        ]);
        return true;
    }

    private static bool TryNullableFromSandboxValue(SandboxValue value, Type type, out object? result)
    {
        if (Nullable.GetUnderlyingType(type) is not { } underlying)
        {
            result = null;
            return false;
        }

        EnsureSupportedNullableUnderlying(underlying);
        if (value is not RecordValue { Fields.Count: 2 } record ||
            record.Fields[0] is not BoolValue hasValue)
        {
            throw new NotSupportedException(
                $"Server extension cannot marshal a sandbox value to nullable type '{type}'.");
        }

        var inner = FromSandboxValue(record.Fields[1], underlying);
        result = hasValue.Value ? inner : null;
        return true;
    }

    private static bool TryNullableFromKernelRpcValue(KernelRpcValue value, Type type, out object? result)
    {
        if (Nullable.GetUnderlyingType(type) is not { } underlying)
        {
            result = null;
            return false;
        }

        EnsureSupportedNullableUnderlying(underlying);
        value.RequireKind(KernelRpcValueKind.Record);
        if (value.ItemCount != 2)
        {
            throw new NotSupportedException(
                $"Server extension cannot marshal a kernel RPC value to nullable type '{type}'.");
        }

        var hasValue = value.GetItem(0).BoolValue;
        var inner = FromKernelRpcValue(value.GetItem(1), underlying);
        result = hasValue ? inner : null;
        return true;
    }

    private static bool TryNullableSandboxType(Type type, int depth, out SandboxType sandboxType)
    {
        if (Nullable.GetUnderlyingType(type) is not { } underlying)
        {
            sandboxType = null!;
            return false;
        }

        if (depth >= MaxTypeNestingDepth)
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' nests beyond the supported depth of {MaxTypeNestingDepth}.");
        }

        EnsureSupportedNullableUnderlying(underlying);
        sandboxType = SandboxType.Record([SandboxType.Bool, SandboxTypeOf(underlying, depth + 1)]);
        return true;
    }

    private static SandboxValue NullableZeroValue(Type underlying)
    {
        if (NullableZeroValueFactories.TryGetValue(underlying, out var factory))
        {
            return factory();
        }

        if (underlying.IsEnum)
        {
            return EnumUsesI64(underlying) ? SandboxValue.FromInt64(0L) : SandboxValue.FromInt32(0);
        }

        throw UnsupportedNullableUnderlying(underlying);
    }

    private static void EnsureSupportedNullableUnderlying(Type underlying)
    {
        if (NullableZeroValueFactories.ContainsKey(underlying) || underlying.IsEnum)
        {
            return;
        }

        throw UnsupportedNullableUnderlying(underlying);
    }

    private static SandboxValue NullableDateTimeZeroValue()
        => SandboxValue.FromOwnedRecord(
        [
            SandboxValue.FromInt64(0L),
            SandboxValue.FromInt64(0L)
        ]);

    private static NotSupportedException UnsupportedNullableUnderlying(Type underlying)
        => new($"Kernel RPC service nullable type '{typeof(Nullable<>).MakeGenericType(underlying)}' is not supported.");

    private static Type? UnsupportedNullableValueType(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return null;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return IsSupportedNullableUnderlying(underlying) ? null : underlying;
        }

        return UnsupportedNestedNullableValueType(type, visited);
    }

    private static Type? UnsupportedNestedNullableValueType(Type type, HashSet<Type> visited)
    {
        if (UnsupportedNullableElementType(type, visited) is { } unsupportedElement)
        {
            return unsupportedElement;
        }

        if (UnsupportedNullableMapType(type, visited) is { } unsupportedMap)
        {
            return unsupportedMap;
        }

        return UnsupportedNullableDtoFieldType(type, visited);
    }

    private static Type? UnsupportedNullableElementType(Type type, HashSet<Type> visited)
        => ElementType(type) is { } elementType
            ? UnsupportedNullableValueType(elementType, visited)
            : null;

    private static Type? UnsupportedNullableMapType(Type type, HashSet<Type> visited)
    {
        if (MapTypes(type) is not { } mapTypes)
        {
            return null;
        }

        if (UnsupportedNullableValueType(mapTypes.Key, visited) is { } unsupportedKey)
        {
            return unsupportedKey;
        }

        return UnsupportedNullableValueType(mapTypes.Value, visited);
    }

    private static Type? UnsupportedNullableDtoFieldType(Type type, HashSet<Type> visited)
    {
        if (DtoShape(type) is { } shape)
        {
            foreach (var field in shape.Fields)
            {
                if (UnsupportedNullableValueType(field.Type, visited) is { } unsupportedField)
                {
                    return unsupportedField;
                }
            }
        }

        return null;
    }

    private static bool ContainsNullableValueType(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (Nullable.GetUnderlyingType(type) is not null)
        {
            return true;
        }

        if (ContainsNullableElementType(type, visited))
        {
            return true;
        }

        if (ContainsNullableMapType(type, visited))
        {
            return true;
        }

        return ContainsNullableDtoFieldType(type, visited);
    }

    private static bool ContainsNullableElementType(Type type, HashSet<Type> visited)
        => ElementType(type) is { } elementType && ContainsNullableValueType(elementType, visited);

    private static bool ContainsNullableMapType(Type type, HashSet<Type> visited)
    {
        if (MapTypes(type) is not { } mapTypes)
        {
            return false;
        }

        if (ContainsNullableValueType(mapTypes.Key, visited))
        {
            return true;
        }

        return ContainsNullableValueType(mapTypes.Value, visited);
    }

    private static bool ContainsNullableDtoFieldType(Type type, HashSet<Type> visited)
    {
        if (DtoShape(type) is { } shape)
        {
            foreach (var field in shape.Fields)
            {
                if (ContainsNullableValueType(field.Type, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSupportedNullableUnderlying(Type underlying)
    {
        try
        {
            EnsureSupportedNullableUnderlying(underlying);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
