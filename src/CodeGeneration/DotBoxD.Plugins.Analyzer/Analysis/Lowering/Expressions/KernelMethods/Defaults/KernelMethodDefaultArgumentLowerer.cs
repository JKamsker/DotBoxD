using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class KernelMethodDefaultArgumentLowerer
{
    public static DotBoxDExpressionModel Lower(IParameterSymbol parameter, object? value)
    {
        if (DotBoxDNullableScalarType.TryGetSupportedUnderlying(parameter.Type, out _))
        {
            return LowerNullableDefaultArgument(parameter, value);
        }

        return LowerScalarDefaultArgument(parameter.Type, value, parameter);
    }

    private static DotBoxDExpressionModel LowerNullableDefaultArgument(IParameterSymbol parameter, object? value)
    {
        if (value is null)
        {
            return new(
                DotBoxDNullableScalarExpressionLowerer.NullSource(parameter.Type),
                DotBoxDGenerationNames.ManifestTypes.Record,
                true);
        }

        var scalar = LowerScalarDefaultArgument(
            ((INamedTypeSymbol)parameter.Type).TypeArguments[0],
            value,
            parameter);
        return new(
            DotBoxDNullableScalarExpressionLowerer.PresentSource(parameter.Type, scalar),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);
    }

    private static DotBoxDExpressionModel LowerScalarDefaultArgument(
        ITypeSymbol parameterType,
        object? value,
        IParameterSymbol parameter)
    {
        var type = DotBoxDTypeNameReader.KernelMethodTypeName(parameterType);
        if (TryLowerEnumDefault(parameterType, value, type) is { } enumDefault)
            return enumDefault;

        if (TryLowerFrameworkValue(parameterType, value, parameter) is { } frameworkValue)
            return frameworkValue;

        if (TryLowerScalarLiteral(type, value) is { } scalarLiteral)
            return scalarLiteral;

        throw UnsupportedDefaultValue(parameter);
    }

    private static DotBoxDExpressionModel? TryLowerEnumDefault(ITypeSymbol parameterType, object? value, string type)
    {
        if (parameterType is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType || value is null)
            return null;

        var raw = EnumDefaultValue(enumType, value);
        return DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
            ? new DotBoxDExpressionModel(
                $"{DotBoxDGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(raw)})",
                type,
                false)
            : new DotBoxDExpressionModel(
                $"{DotBoxDGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(unchecked((int)raw))})",
                type,
                false);
    }

    private static DotBoxDExpressionModel? TryLowerFrameworkValue(
        ITypeSymbol parameterType,
        object? value,
        IParameterSymbol parameter)
    {
        if (parameterType.SpecialType == SpecialType.System_DateTime && value is DateTime dateTime)
            return LowerDateTimeDefault(parameterType, dateTime, parameter);

        if (DotBoxDRpcTypeMapper.IsDecimalWireType(parameterType) && value is decimal decimalValue)
            return new(DecimalRecord(parameterType, decimalValue), DotBoxDGenerationNames.ManifestTypes.Record, true);

        if (value is null)
            return TryLowerFrameworkDefault(parameterType);

        return value is Guid guid && guid == Guid.Empty ? TryLowerFrameworkDefault(parameterType) : null;
    }

    private static DotBoxDExpressionModel? TryLowerScalarLiteral(string type, object? value)
        => TryLowerBool(type, value) ??
           TryLowerInt(type, value) ??
           TryLowerLong(type, value) ??
           TryLowerDouble(type, value) ??
           TryLowerString(type, value);

    private static DotBoxDExpressionModel? TryLowerBool(string type, object? value)
        => type == DotBoxDGenerationNames.ManifestTypes.Bool && value is bool boolean
            ? new($"{DotBoxDGenerationNames.Helpers.Bool}({LiteralReader.ObjectLiteral(boolean)})", type, false)
            : null;

    private static DotBoxDExpressionModel? TryLowerInt(string type, object? value)
        => type == DotBoxDGenerationNames.ManifestTypes.Int && value is int number
            ? new($"{DotBoxDGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(number)})", type, false)
            : null;

    private static DotBoxDExpressionModel? TryLowerLong(string type, object? value)
        => type == DotBoxDGenerationNames.ManifestTypes.Long
            ? TryLowerLongValue(value, type)
            : null;

    private static DotBoxDExpressionModel? TryLowerLongValue(object? value, string type)
        => value switch
        {
            int number => new($"{DotBoxDGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral((long)number)})", type, false),
            long number => new($"{DotBoxDGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(number)})", type, false),
            _ => null,
        };

    private static DotBoxDExpressionModel? TryLowerDouble(string type, object? value)
        => type == DotBoxDGenerationNames.ManifestTypes.Double
            ? TryLowerDoubleValue(value, type)
            : null;

    private static DotBoxDExpressionModel? TryLowerDoubleValue(object? value, string type)
        => value switch
        {
            int number => F64((double)number, type),
            long number => F64((double)number, type),
            float number when IsFinite(number) => F64(number, type),
            double number when IsFinite(number) => F64(number, type),
            _ => null,
        };

    private static DotBoxDExpressionModel? TryLowerString(string type, object? value)
        => type == DotBoxDGenerationNames.ManifestTypes.String && value is string text
            ? new($"{DotBoxDGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(text)})", type, true)
            : null;

    private static DotBoxDExpressionModel F64(double value, string type)
        => new($"{DotBoxDGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral(value)})", type, false);

    private static NotSupportedException UnsupportedDefaultValue(IParameterSymbol parameter)
        => new($"[KernelMethod] '{parameter.ContainingSymbol.Name}' optional parameter '{parameter.Name}' has an unsupported default value.");

    private static DotBoxDExpressionModel LowerDateTimeDefault(
        ITypeSymbol type,
        DateTime value,
        IParameterSymbol parameter)
    {
        if (value.Kind != DateTimeKind.Unspecified)
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{parameter.ContainingSymbol.Name}' optional parameter '{parameter.Name}' DateTime default must use DateTimeKind.Unspecified.");
        }

        return new(
            DateTimeRecord(type, LiteralReader.ObjectLiteral(value.Ticks), DotBoxDGenerationNames.CSharpLiterals.Int64Default),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);
    }

    private static DotBoxDExpressionModel? TryLowerFrameworkDefault(ITypeSymbol type)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return new(
                $"new {DotBoxDGenerationNames.TypeNames.GlobalLiteralExpression}({DotBoxDGenerationNames.TypeNames.GlobalSandboxValue}.FromGuid(global::System.Guid.Empty), Span)",
                DotBoxDGenerationNames.ManifestTypes.Guid,
                false);
        }

        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            return new(DateTimeRecordDefault(type), DotBoxDGenerationNames.ManifestTypes.Record, true);
        }

        if (DotBoxDRpcTypeMapper.IsDecimalWireType(type))
        {
            return new(DecimalRecord(type, default), DotBoxDGenerationNames.ManifestTypes.Record, true);
        }

        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(type))
            return new(
                $"{DotBoxDGenerationNames.Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
                DotBoxDGenerationNames.ManifestTypes.Int,
                false);

        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type) || DotBoxDRpcTypeMapper.IsTimeSpanWireType(type))
            return new(
                $"{DotBoxDGenerationNames.Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
                DotBoxDGenerationNames.ManifestTypes.Long,
                false);

        return null;
    }

    private static string DateTimeRecordDefault(ITypeSymbol type)
        => DateTimeRecord(
            type,
            DotBoxDGenerationNames.CSharpLiterals.Int64Default,
            DotBoxDGenerationNames.CSharpLiterals.Int64Default);

    private static string DateTimeRecord(ITypeSymbol type, string utcTicks, string offsetTicks)
        => DotBoxDRecordCreationExpressionLowerer.RecordNew(
            [
                $"{DotBoxDGenerationNames.Helpers.I64}({utcTicks})",
                $"{DotBoxDGenerationNames.Helpers.I64}({offsetTicks})"
            ],
            SandboxTypeSourceEmitter.TryEmit(type) ?? throw new NotSupportedException());

    private static string DecimalRecord(ITypeSymbol type, decimal value)
        => DotBoxDDecimalWireSource.RecordSource(type, value);

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    private static long EnumDefaultValue(INamedTypeSymbol enumType, object value)
        => enumType.EnumUnderlyingType?.SpecialType switch
        {
            SpecialType.System_UInt64 => unchecked((long)(ulong)value),
            SpecialType.System_UInt32 => (uint)value,
            SpecialType.System_Int64 => (long)value,
            SpecialType.System_Int32 => (int)value,
            SpecialType.System_UInt16 => (ushort)value,
            SpecialType.System_Int16 => (short)value,
            SpecialType.System_Byte => (byte)value,
            SpecialType.System_SByte => (sbyte)value,
            _ => throw new NotSupportedException(
                $"[KernelMethod] '{enumType.ToDisplayString()}' enum default value is not supported.")
        };
}
