using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.CodeGeneration.Shared.Defaults;

internal static class CSharpLiteralFormatter
{
    private static readonly System.Collections.Generic.Dictionary<System.Type, PrimitiveLiteralFormatter> s_primitiveFormatters = new()
    {
        [typeof(bool)] = static (value, _) => (bool)value ? "true" : "false",
        [typeof(string)] = static (value, _) => "\"" + EscapeStringLiteral((string)value) + "\"",
        [typeof(char)] = static (value, _) => "'" + EscapeCharLiteral((char)value) + "'",
        [typeof(sbyte)] = static (value, _) => ((sbyte)value).ToString(CultureInfo.InvariantCulture),
        [typeof(byte)] = static (value, _) => ((byte)value).ToString(CultureInfo.InvariantCulture),
        [typeof(short)] = static (value, _) => ((short)value).ToString(CultureInfo.InvariantCulture),
        [typeof(ushort)] = static (value, _) => ((ushort)value).ToString(CultureInfo.InvariantCulture),
        [typeof(int)] = static (value, _) => ((int)value).ToString(CultureInfo.InvariantCulture),
        [typeof(uint)] = static (value, options) => ((uint)value).ToString(CultureInfo.InvariantCulture) + Suffix("U", options),
        [typeof(long)] = static (value, options) => ((long)value).ToString(CultureInfo.InvariantCulture) + Suffix("L", options),
        [typeof(ulong)] = static (value, options) => ((ulong)value).ToString(CultureInfo.InvariantCulture) + Suffix("UL", options),
        [typeof(float)] = static (value, options) => FormatSingleLiteral((float)value, options),
        [typeof(double)] = static (value, options) => FormatDoubleLiteral((double)value, options),
        [typeof(decimal)] = static (value, options) => ((decimal)value).ToString(CultureInfo.InvariantCulture) + Suffix("M", options),
    };

    private static readonly System.Collections.Generic.Dictionary<SpecialType, System.Func<object, long>> s_signedEnumConverters = new()
    {
        [SpecialType.System_UInt64] = static value => unchecked((long)(ulong)value),
        [SpecialType.System_UInt32] = static value => unchecked((int)(uint)value),
        [SpecialType.System_Int64] = static value => (long)value,
        [SpecialType.System_Int32] = static value => (int)value,
        [SpecialType.System_UInt16] = static value => (ushort)value,
        [SpecialType.System_Int16] = static value => (short)value,
        [SpecialType.System_Byte] = static value => (byte)value,
        [SpecialType.System_SByte] = static value => (sbyte)value,
    };

    private static readonly System.Collections.Generic.Dictionary<char, string> s_charEscapes = new()
    {
        ['\''] = "\\'",
        ['\\'] = "\\\\",
        ['\0'] = "\\0",
        ['\a'] = "\\a",
        ['\b'] = "\\b",
        ['\f'] = "\\f",
        ['\n'] = "\\n",
        ['\r'] = "\\r",
        ['\t'] = "\\t",
        ['\v'] = "\\v",
    };

    private static readonly System.Collections.Generic.Dictionary<char, string> s_stringEscapes = new()
    {
        ['\\'] = "\\\\",
        ['"'] = "\\\"",
        ['\a'] = "\\a",
        ['\b'] = "\\b",
        ['\f'] = "\\f",
        ['\v'] = "\\v",
        ['\r'] = "\\r",
        ['\n'] = "\\n",
        ['\u0085'] = "\\u0085",
        ['\u2028'] = "\\u2028",
        ['\u2029'] = "\\u2029",
        ['\t'] = "\\t",
        ['\0'] = "\\0",
    };

    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private delegate string PrimitiveLiteralFormatter(object value, DefaultLiteralOptions options);

    public static string? FormatValue(object? value, ITypeSymbol type, DefaultLiteralOptions options)
    {
        if (value is null)
        {
            return type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? "null"
                : "default";
        }

        var enumType = EnumType(type);
        if (enumType is not null)
        {
            return FormatEnumLiteral(enumType, value, options);
        }

        if (FormatPrimitiveLiteral(value, options) is { } literal)
        {
            return literal;
        }

        return type.IsValueType && IsRuntimeDefaultValue(value) ? "default" : null;
    }

    public static bool TryFormatAttributeValue(TypedConstant argument, out string literal)
    {
        if (argument.IsNull)
        {
            literal = "null";
            return true;
        }

        if (argument.Kind == TypedConstantKind.Enum &&
            argument.Type is not null &&
            argument.Value is not null &&
            FormatPrimitiveLiteral(argument.Value, DefaultLiteralOptions.SourceGenerator) is { } enumValue)
        {
            literal = "(" + argument.Type.ToDisplayString(s_qualifiedFormat) + ")" + enumValue;
            return true;
        }

        if (argument.Value is not null &&
            FormatPrimitiveLiteral(argument.Value, DefaultLiteralOptions.SourceGenerator) is { } value)
        {
            literal = value;
            return true;
        }

        literal = string.Empty;
        return false;
    }

    public static string? FormatPrimitiveLiteral(object value, DefaultLiteralOptions options)
        => s_primitiveFormatters.TryGetValue(value.GetType(), out var formatter)
            ? formatter(value, options)
            : null;

    public static string EscapeStringLiteral(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            AppendEscapedStringCharacter(builder, c);
        }

        return builder.ToString();
    }

    private static ITypeSymbol? EnumType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            return type;
        }

        return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable &&
            nullable.TypeArguments[0].TypeKind == TypeKind.Enum
                ? nullable.TypeArguments[0]
                : null;
    }

    private static string? FormatEnumLiteral(ITypeSymbol enumType, object value, DefaultLiteralOptions options)
    {
        var enumValue = options.UseUncheckedEnumCasts && enumType is INamedTypeSymbol namedEnum
            ? FormatSignedEnumLiteral(namedEnum, value, options)
            : FormatPrimitiveLiteral(value, options);
        if (enumValue is null)
        {
            return null;
        }

        var cast = "(" + enumType.ToDisplayString(s_qualifiedFormat) + ")" + enumValue;
        return options.UseUncheckedEnumCasts ? "unchecked(" + cast + ")" : cast;
    }

    private static string FormatSignedEnumLiteral(
        INamedTypeSymbol enumType,
        object value,
        DefaultLiteralOptions options)
    {
        var underlying = enumType.EnumUnderlyingType?.SpecialType;
        if (underlying is null || !s_signedEnumConverters.TryGetValue(underlying.Value, out var converter))
        {
            throw new System.NotSupportedException(
                $"Enum literal values for '{enumType.ToDisplayString()}' are not supported.");
        }

        var raw = converter(value);
        return raw.ToString(CultureInfo.InvariantCulture) +
            (enumType.EnumUnderlyingType?.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64
                ? Suffix("L", options)
                : string.Empty);
    }

    private static string FormatSingleLiteral(float value, DefaultLiteralOptions options)
    {
        if (float.IsNaN(value))
        {
            return "global::System.Single.NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "global::System.Single.PositiveInfinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "global::System.Single.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + Suffix("F", options);
    }

    private static string FormatDoubleLiteral(double value, DefaultLiteralOptions options)
    {
        if (double.IsNaN(value))
        {
            return "global::System.Double.NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "global::System.Double.PositiveInfinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "global::System.Double.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + Suffix("D", options);
    }

    private static string EscapeCharLiteral(char c)
    {
        if (s_charEscapes.TryGetValue(c, out var escaped))
        {
            return escaped;
        }

        return char.IsControl(c) || c is '\u2028' or '\u2029'
            ? UnicodeEscape(c)
            : c.ToString();
    }

    private static void AppendEscapedStringCharacter(StringBuilder builder, char c)
    {
        if (s_stringEscapes.TryGetValue(c, out var escaped))
        {
            builder.Append(escaped);
            return;
        }

        builder.Append(char.IsControl(c) ? UnicodeEscape(c) : c);
    }

    private static string UnicodeEscape(char c)
        => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);

    private static bool IsRuntimeDefaultValue(object value)
    {
        var type = value.GetType();
        return Equals(value, System.Activator.CreateInstance(type));
    }

    private static string Suffix(string suffix, DefaultLiteralOptions options)
        => options.LowercaseNumericSuffixes ? suffix.ToLowerInvariant() : suffix;
}
