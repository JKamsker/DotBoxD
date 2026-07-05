using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DotBoxD.CodeGeneration.Shared.Defaults;

internal static class ParameterDefaultValueEmitter
{
    private static readonly System.Collections.Generic.Dictionary<SpecialType, object> RuntimeDefaults = new()
    {
        [SpecialType.System_Boolean] = false,
        [SpecialType.System_Char] = '\0',
        [SpecialType.System_SByte] = (sbyte)0,
        [SpecialType.System_Byte] = (byte)0,
        [SpecialType.System_Int16] = (short)0,
        [SpecialType.System_UInt16] = (ushort)0,
        [SpecialType.System_Int32] = 0,
        [SpecialType.System_UInt32] = 0U,
        [SpecialType.System_Int64] = 0L,
        [SpecialType.System_UInt64] = 0UL,
        [SpecialType.System_Single] = 0F,
        [SpecialType.System_Double] = 0D,
        [SpecialType.System_Decimal] = 0M,
        [SpecialType.System_DateTime] = default(System.DateTime),
    };

    public static bool HasDefaultValue(IParameterSymbol parameter)
        => parameter.HasExplicitDefaultValue ||
           ParameterDefaultMetadataReader.HasOptionalMetadata(parameter);

    public static bool ShouldPreserveOptionalAttributeDefault(IMethodSymbol method, int parameterIndex)
    {
        var parameter = method.Parameters[parameterIndex];
        if (!ParameterDefaultMetadataReader.HasOptionalMetadata(parameter))
        {
            return false;
        }

        for (var i = parameterIndex + 1; i < method.Parameters.Length; i++)
        {
            if (!HasDefaultValue(method.Parameters[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static string? FormatSignatureDefaultLiteral(
        IParameterSymbol parameter,
        bool hasDefaultValue,
        DefaultLiteralOptions options)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return hasDefaultValue ? "default" : null;
        }

        return CSharpLiteralFormatter.FormatValue(parameter.ExplicitDefaultValue, parameter.Type, options);
    }

    public static string FormatMetadataDefaultValueExpression(
        IParameterSymbol parameter,
        bool hasDefaultValue,
        string defaultValueLiteral)
    {
        if (!hasDefaultValue)
        {
            return string.Empty;
        }

        if (ParameterDefaultMetadataReader.TryGetDateTimeConstantTicks(parameter, out var ticks))
        {
            return "new global::System.DateTime(" +
                ticks.ToString(CultureInfo.InvariantCulture) +
                "L)";
        }

        var decimalConstant =
            ParameterDefaultMetadataReader.FormatDecimalConstantMetadataDefaultValueExpression(parameter);
        if (decimalConstant.Length > 0)
        {
            return decimalConstant;
        }

        if (ParameterDefaultMetadataReader.TryFormatDefaultParameterValueAttributeLiteral(
            parameter,
            out var attributeLiteral))
        {
            return attributeLiteral;
        }

        if (defaultValueLiteral.Length == 0 &&
            ParameterDefaultMetadataReader.HasOptionalMetadata(parameter))
        {
            return "default";
        }

        return defaultValueLiteral;
    }

    public static bool HasDateTimeConstantAttribute(IParameterSymbol parameter)
        => ParameterDefaultMetadataReader.HasDateTimeConstantAttribute(parameter);

    public static bool HasDecimalConstantAttribute(IParameterSymbol parameter)
        => ParameterDefaultMetadataReader.HasDecimalConstantAttribute(parameter);

    public static bool HasDefaultParameterValueAttribute(IParameterSymbol parameter)
        => ParameterDefaultMetadataReader.HasDefaultParameterValueAttribute(parameter);

    public static string FormatDateTimeConstantAttribute(IParameterSymbol parameter)
        => ParameterDefaultMetadataReader.FormatDateTimeConstantAttribute(parameter);

    public static string FormatDecimalConstantAttribute(IParameterSymbol parameter)
        => ParameterDefaultMetadataReader.FormatDecimalConstantAttribute(parameter);

    public static string FormatDefaultParameterValueAttribute(IParameterSymbol parameter)
        => ParameterDefaultMetadataReader.FormatDefaultParameterValueAttribute(parameter);

    public static string FormatMetadataDefaultAttributePrefix(
        IParameterSymbol parameter,
        bool includeOptionalAttribute)
    {
        var prefix = includeOptionalAttribute
            ? "[global::System.Runtime.InteropServices.OptionalAttribute] "
            : string.Empty;
        return prefix +
            FormatDateTimeConstantAttribute(parameter) +
            FormatDecimalConstantAttribute(parameter) +
            FormatDefaultParameterValueAttribute(parameter);
    }

    public static string ParameterDefaultClause(
        IParameterSymbol parameter,
        DefaultLiteralOptions options)
    {
        var hasDefaultValue = HasDefaultValue(parameter);
        var literal = FormatSignatureDefaultLiteral(parameter, hasDefaultValue, options);
        return literal is null ? string.Empty : " = " + literal;
    }

    public static bool TryGetRuntimeDefaultValue(IParameterSymbol parameter, out object? value)
    {
        if (parameter.HasExplicitDefaultValue)
        {
            return TryGetExplicitRuntimeDefaultValue(parameter, out value);
        }

        return TryGetMetadataRuntimeDefaultValue(parameter, out value);
    }

    private static bool TryGetExplicitRuntimeDefaultValue(IParameterSymbol parameter, out object? value)
    {
        var explicitDefaultValue = parameter.ExplicitDefaultValue;
        if (explicitDefaultValue is not null ||
            parameter.Type.IsReferenceType ||
            parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            value = explicitDefaultValue;
            return true;
        }

        if (TryGetRuntimeDefaultValue(parameter.Type, out value))
        {
            return true;
        }

        value = explicitDefaultValue;
        return true;
    }

    private static bool TryGetMetadataRuntimeDefaultValue(IParameterSymbol parameter, out object? value)
    {
        if (!ParameterDefaultMetadataReader.HasOptionalMetadata(parameter))
        {
            value = null;
            return false;
        }

        if (TryGetDateTimeConstantDefault(parameter, out var dateTime))
        {
            value = dateTime;
            return true;
        }

        if (parameter.Type.SpecialType == SpecialType.System_Decimal &&
            ParameterDefaultMetadataReader.TryGetDecimalConstantDefault(parameter, out var decimalValue))
        {
            value = decimalValue;
            return true;
        }

        if (ParameterDefaultMetadataReader.TryGetDefaultParameterValueAttributeValue(parameter, out value))
        {
            return true;
        }

        if (ParameterDefaultMetadataReader.HasUnresolvedMetadataDefaultAttribute(parameter))
        {
            value = null;
            return false;
        }

        return TryGetRuntimeDefaultValue(parameter.Type, out value);
    }

    private static bool TryGetDateTimeConstantDefault(IParameterSymbol parameter, out System.DateTime value)
    {
        if (parameter.Type.SpecialType == SpecialType.System_DateTime &&
            ParameterDefaultMetadataReader.TryGetDateTimeConstantTicks(parameter, out var ticks) &&
            ticks >= System.DateTime.MinValue.Ticks &&
            ticks <= System.DateTime.MaxValue.Ticks)
        {
            value = new System.DateTime(ticks);
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetRuntimeDefaultValue(ITypeSymbol type, out object? value)
    {
        if (type.IsReferenceType ||
            type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            value = null;
            return true;
        }

        if (RuntimeDefaults.TryGetValue(type.SpecialType, out value))
        {
            return true;
        }

        if (type.Name == nameof(System.Guid) &&
            string.Equals(type.ContainingNamespace.ToDisplayString(), nameof(System), System.StringComparison.Ordinal))
        {
            value = default(System.Guid);
            return true;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return TryGetEnumRuntimeDefaultValue(enumType, out value);
        }

        value = null;
        return false;
    }

    private static bool TryGetEnumRuntimeDefaultValue(INamedTypeSymbol enumType, out object value)
    {
        if (enumType.EnumUnderlyingType is { } underlying &&
            RuntimeDefaults.TryGetValue(underlying.SpecialType, out value!))
        {
            return true;
        }

        value = 0;
        return false;
    }
}
