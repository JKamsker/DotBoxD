using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static readonly Dictionary<Type, ScalarToSandboxConverter> ScalarToSandboxConverters = new()
    {
        [typeof(bool)] = static value => SandboxValue.FromBool((bool)value!),
        [typeof(int)] = static value => SandboxValue.FromInt32((int)value!),
        [typeof(long)] = static value => SandboxValue.FromInt64((long)value!),
        [typeof(double)] = static value => SandboxValue.FromDouble((double)value!),
        [typeof(float)] = static value => SandboxValue.FromDouble((float)value!),
        [typeof(string)] = static value => SandboxValue.FromString((string)value!),
        [typeof(Guid)] = static value => SandboxValue.FromGuid((Guid)value!),
        [typeof(DateOnly)] = static value => SandboxValue.FromInt32(((DateOnly)value!).DayNumber),
        [typeof(TimeOnly)] = static value => SandboxValue.FromInt64(((TimeOnly)value!).Ticks),
        [typeof(TimeSpan)] = static value => SandboxValue.FromInt64(((TimeSpan)value!).Ticks),
        [typeof(CancellationToken)] = static value => SandboxValue.FromBool(((CancellationToken)value!).IsCancellationRequested),
    };

    private static readonly Dictionary<Type, ScalarFromSandboxConverter> ScalarFromSandboxConverters = new()
    {
        [typeof(bool)] = static (SandboxValue value, out object? result) => TryUnbox<BoolValue>(value, out result, static item => item.Value),
        [typeof(int)] = static (SandboxValue value, out object? result) => TryUnbox<I32Value>(value, out result, static item => item.Value),
        [typeof(long)] = static (SandboxValue value, out object? result) => TryUnbox<I64Value>(value, out result, static item => item.Value),
        [typeof(double)] = static (SandboxValue value, out object? result) => TryUnbox<F64Value>(value, out result, static item => item.Value),
        [typeof(float)] = static (SandboxValue value, out object? result) =>
            TryUnbox<F64Value>(value, out result, static item => DoubleToSingle(item.Value)),
        [typeof(string)] = static (SandboxValue value, out object? result) => TryUnbox<StringValue>(value, out result, static item => item.Value),
        [typeof(Guid)] = static (SandboxValue value, out object? result) => TryUnbox<GuidValue>(value, out result, static item => item.Value),
        [typeof(DateOnly)] = static (SandboxValue value, out object? result) =>
            TryUnbox<I32Value>(value, out result, static item => DateOnlyFromDayNumber(item.Value)),
        [typeof(TimeOnly)] = static (SandboxValue value, out object? result) =>
            TryUnbox<I64Value>(value, out result, static item => TimeOnlyFromTicks(item.Value)),
        [typeof(TimeSpan)] = static (SandboxValue value, out object? result) =>
            TryUnbox<I64Value>(value, out result, static item => new TimeSpan(item.Value)),
        [typeof(CancellationToken)] = static (SandboxValue value, out object? result) =>
            TryUnbox<BoolValue>(value, out result, static item => new CancellationToken(item.Value)),
    };

    private delegate SandboxValue ScalarToSandboxConverter(object? value);

    private delegate bool ScalarFromSandboxConverter(SandboxValue value, out object? result);

    private static SandboxValue? TryScalarToSandbox(object? value, Type type)
        => ScalarToSandboxConverters.TryGetValue(type, out var convert) ? convert(value) : null;

    private static bool TryScalarFromSandbox(SandboxValue value, Type type, out object? result)
    {
        if (ScalarFromSandboxConverters.TryGetValue(type, out var convert))
        {
            return convert(value, out result);
        }

        result = null;
        return false;
    }

    private static bool TryUnbox<TValue>(
        SandboxValue value,
        out object? result,
        Func<TValue, object> convert)
        where TValue : SandboxValue
    {
        if (value is TValue typed)
        {
            result = convert(typed);
            return true;
        }

        result = null;
        return false;
    }

    private static Array ToArray(IReadOnlyList<SandboxValue> values, Type elementType)
    {
        var array = Array.CreateInstance(elementType, values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            array.SetValue(FromSandboxValue(values[i], elementType), i);
        }

        return array;
    }

    // An enum marshals through its underlying integer; widths that overflow I32 (uint/long/ulong) use I64.
    private static bool EnumUsesI64(Type type)
    {
        var underlying = Enum.GetUnderlyingType(type);
        return underlying == typeof(uint) || underlying == typeof(long) || underlying == typeof(ulong);
    }

    // A declared ulong-backed enum value above long.MaxValue is carried as a negative I64 wire value. Reject
    // undeclared high-bit values here so outbound encode cannot produce a value inbound decode rejects.
    private static long EnumToInt64(object value, Type type)
        => Enum.GetUnderlyingType(type) == typeof(ulong)
            ? UInt64EnumToInt64(value, type)
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

    private static long UInt64EnumToInt64(object value, Type type)
    {
        var bits = Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        if ((bits & (1UL << 63)) != 0UL && !NegativeBitsMatchUInt64Enum(type, bits))
        {
            throw EnumOutOfRange(type, unchecked((long)bits));
        }

        return unchecked((long)bits);
    }
}
