using System.Globalization;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Lifecycle;

internal static class LiveSettingTypeConverter
{
    private static readonly Dictionary<Type, string> ClrTypeNames = new()
    {
        [typeof(bool)] = PluginManifestNames.LiveSettingTypes.Bool,
        [typeof(int)] = PluginManifestNames.LiveSettingTypes.Int,
        [typeof(long)] = PluginManifestNames.LiveSettingTypes.Long,
        [typeof(double)] = PluginManifestNames.LiveSettingTypes.Double,
        [typeof(string)] = PluginManifestNames.LiveSettingTypes.String,
    };

    private static readonly Dictionary<Type, Func<object?, object?>> ClrCoercers = new()
    {
        [typeof(string)] = static value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        [typeof(bool)] = static value => value is string text
            ? bool.Parse(text)
            : Convert.ToBoolean(value, CultureInfo.InvariantCulture),
        [typeof(int)] = CoerceInt32,
        [typeof(long)] = static value => value is string text
            ? long.Parse(text, CultureInfo.InvariantCulture)
            : ToExactInt64(value),
        [typeof(double)] = static value => FiniteDouble(value),
    };

    private static readonly Dictionary<Type, Func<object, long>> ExactInt64Converters = new()
    {
        [typeof(bool)] = static _ => throw new InvalidCastException(),
        [typeof(sbyte)] = static value => (sbyte)value,
        [typeof(byte)] = static value => (byte)value,
        [typeof(short)] = static value => (short)value,
        [typeof(ushort)] = static value => (ushort)value,
        [typeof(int)] = static value => (int)value,
        [typeof(uint)] = static value => (uint)value,
        [typeof(long)] = static value => (long)value,
        [typeof(ulong)] = static value => checked((long)(ulong)value),
        [typeof(double)] = static value => ToExactInt64FromDouble((double)value),
        [typeof(float)] = static value => ToExactInt64FromDouble((float)value),
        [typeof(decimal)] = static value =>
            decimal.ToInt64(decimal.Truncate((decimal)value) == (decimal)value ? (decimal)value : throw new OverflowException()),
        [typeof(string)] = static value => long.Parse((string)value, CultureInfo.InvariantCulture),
    };

    public static string FromClrType(Type type)
    {
        var actual = RequireSupportedClrType(type);
        return ClrTypeNames.TryGetValue(actual, out var name)
            ? name
            : throw Diagnostic($"Live setting type '{type.Name}' is not supported.");
    }

    public static SandboxType ToSandboxType(string type)
        => type switch
        {
            PluginManifestNames.LiveSettingTypes.Bool => SandboxType.Bool,
            PluginManifestNames.LiveSettingTypes.Int => SandboxType.I32,
            PluginManifestNames.LiveSettingTypes.Long => SandboxType.I64,
            PluginManifestNames.LiveSettingTypes.Double => SandboxType.F64,
            PluginManifestNames.LiveSettingTypes.String => SandboxType.String,
            _ => throw Diagnostic($"Live setting type '{type}' is not supported.")
        };

    public static object? DefaultFor(Type type)
    {
        var actual = RequireSupportedClrType(type);
        if (actual == typeof(string))
        {
            return string.Empty;
        }

        return Activator.CreateInstance(actual);
    }

    public static object? CoerceClr(Type targetType, object? value)
    {
        var actual = RequireSupportedClrType(targetType);
        try
        {
            return CoerceClrCore(actual, value);
        }
        catch (SandboxValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or OverflowException)
        {
            throw Diagnostic($"Live setting value is not valid for type '{TypeName(actual)}'.");
        }
    }

    private static object? CoerceClrCore(Type actual, object? value)
    {
        if (value is null)
        {
            return actual == typeof(string) ? string.Empty : Activator.CreateInstance(actual);
        }

        if (ClrCoercers.TryGetValue(actual, out var coercer))
        {
            return coercer(value);
        }

        throw Diagnostic($"Live setting type '{actual.Name}' is not supported.");
    }

    private static object CoerceInt32(object? value)
    {
        var asLong = value is string text
            ? long.Parse(text, CultureInfo.InvariantCulture)
            : ToExactInt64(value);
        if (asLong is < int.MinValue or > int.MaxValue)
        {
            throw new OverflowException();
        }

        return (int)asLong;
    }

    public static object? CoerceClr(string type, object? value)
        => type switch
        {
            PluginManifestNames.LiveSettingTypes.Bool => CoerceClr(typeof(bool), value),
            PluginManifestNames.LiveSettingTypes.Int => CoerceClr(typeof(int), value),
            PluginManifestNames.LiveSettingTypes.Long => CoerceClr(typeof(long), value),
            PluginManifestNames.LiveSettingTypes.Double => CoerceClr(typeof(double), value),
            PluginManifestNames.LiveSettingTypes.String => CoerceClr(typeof(string), value),
            _ => throw Diagnostic($"Live setting type '{type}' is not supported.")
        };

    public static SandboxValue ToSandboxValue(string type, object? value)
        => type switch
        {
            PluginManifestNames.LiveSettingTypes.Bool => SandboxValue.FromBool((bool)CoerceClr(typeof(bool), value)!),
            PluginManifestNames.LiveSettingTypes.Int => SandboxValue.FromInt32((int)CoerceClr(typeof(int), value)!),
            PluginManifestNames.LiveSettingTypes.Long => SandboxValue.FromInt64((long)CoerceClr(typeof(long), value)!),
            PluginManifestNames.LiveSettingTypes.Double => SandboxValue.FromDouble((double)CoerceClr(typeof(double), value)!),
            PluginManifestNames.LiveSettingTypes.String => SandboxValue.FromString((string)CoerceClr(typeof(string), value)!),
            _ => throw Diagnostic($"Live setting type '{type}' is not supported.")
        };

    public static void ValidateDefinition(LiveSettingDefinition definition)
    {
        _ = ToSandboxType(definition.Type);
        var defaultValue = CoerceClr(definition.Type, definition.DefaultValue);
        ValidateRangeValue(definition, defaultValue);
    }

    public static void ValidateRangeDefinition(LiveSettingDefinition definition)
    {
        if (definition.Min is null && definition.Max is null)
        {
            return;
        }

        if (!PluginManifestNames.LiveSettingTypes.IsNumeric(definition.Type))
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK022", $"Live setting '{definition.Name}' has a range on non-numeric type '{definition.Type}'.")
            ]);
        }

        if (definition.Min is not null && definition.Max is not null &&
            CompareNumeric(definition.Min, definition.Max) > 0)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK024", $"Live setting '{definition.Name}' has a minimum greater than its maximum.")
            ]);
        }
    }

    public static void ValidateRangeValue(LiveSettingDefinition definition, object? value)
    {
        ValidateRangeDefinition(definition);
        if (definition.Min is not null && CompareNumeric(value, definition.Min) < 0)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK023", $"Live setting '{definition.Name}' is below its allowed range.")
            ]);
        }

        if (definition.Max is not null && CompareNumeric(value, definition.Max) > 0)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK023", $"Live setting '{definition.Name}' is above its allowed range.")
            ]);
        }
    }

    public static Exception Diagnostic(string message)
        => new SandboxValidationException([new SandboxDiagnostic("DBXK020", message)]);

    private static Type RequireSupportedClrType(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is not null || type.IsEnum)
        {
            throw Diagnostic($"Live setting type '{type.Name}' is not supported.");
        }

        return type;
    }

    private static long ToExactInt64(object? value)
    {
        if (value is null)
        {
            return 0L;
        }

        return ExactInt64Converters.TryGetValue(value.GetType(), out var converter)
            ? converter(value)
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static long ToExactInt64FromDouble(double value)
    {
        // long.MaxValue (2^63-1) is not representable as a double; the smallest double above
        // the long range is 2^63, so the upper bound must be strictly less than 9223372036854775808.0.
        const double ExclusiveUpperBound = 9223372036854775808.0;
        if (!double.IsFinite(value) || Math.Floor(value) != value ||
            value < long.MinValue || value >= ExclusiveUpperBound)
        {
            throw new OverflowException();
        }

        return (long)value;
    }

    // Compares two numeric live-setting operands using exact integer arithmetic when both
    // are integral so 64-bit boundaries near and above 2^53 are not collapsed through double.
    private static int CompareNumeric(object? left, object? right)
    {
        if (TryAsExactInt64(left, out var leftLong) && TryAsExactInt64(right, out var rightLong))
        {
            return leftLong.CompareTo(rightLong);
        }

        return FiniteDouble(left).CompareTo(FiniteDouble(right));
    }

    private static bool TryAsExactInt64(object? value, out long result)
    {
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                result = ToExactInt64(value);
                return true;
            case ulong v when v <= long.MaxValue:
                result = (long)v;
                return true;
            default:
                result = 0L;
                return false;
        }
    }

    private static double FiniteDouble(object? value)
    {
        var result = value is string text
            ? double.Parse(text, CultureInfo.InvariantCulture)
            : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(result))
        {
            throw Diagnostic("Live setting value must be a finite number.");
        }

        return result;
    }

    private static string TypeName(Type type)
        => ClrTypeNames.TryGetValue(type, out var name) ? name : type.Name;
}
