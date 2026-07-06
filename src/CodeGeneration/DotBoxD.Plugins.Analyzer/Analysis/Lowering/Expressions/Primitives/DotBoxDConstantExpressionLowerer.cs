using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDConstantExpressionLowerer
{
    public static DotBoxDExpressionModel? TryLower(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => TryLower(expression, semanticModel, cancellationToken, targetType: null);

    public static DotBoxDExpressionModel? TryLower(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? targetType)
    {
        if (TryLowerDefaultValue(expression, semanticModel, cancellationToken, targetType) is { } defaultValue)
        {
            return defaultValue;
        }

        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (!constant.HasValue)
        {
            return null;
        }

        var type = semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType;
        if (type is null)
        {
            throw new NotSupportedException($"Unsupported plugin constant expression '{expression}'.");
        }

        if (TryLowerEnumConstant(targetType, constant.Value, type, out var enumConstant))
        {
            return enumConstant;
        }

        if (TryLowerDecimalConstant(targetType, constant.Value, type, out var decimalConstant))
        {
            return decimalConstant;
        }

        return Lower(expression, constant.Value, targetType ?? DotBoxDTypeNameReader.SandboxTypeName(type));
    }

    private static bool TryLowerEnumConstant(
        string? targetType,
        object? value,
        ITypeSymbol type,
        out DotBoxDExpressionModel model)
    {
        model = null!;
        if (targetType is not null)
        {
            return false;
        }

        if (type.TypeKind != TypeKind.Enum || type is not INamedTypeSymbol enumType)
        {
            return false;
        }

        // An enum constant lowers to the same I32/I64 representation an enum property or DTO field carries.
        model = DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
            ? Int64(EnumConstantToInt64(value, enumType))
            : Int32(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    private static bool TryLowerDecimalConstant(
        string? targetType,
        object? value,
        ITypeSymbol type,
        out DotBoxDExpressionModel model)
    {
        model = null!;
        if (!DotBoxDRpcTypeMapper.IsDecimalWireType(type))
        {
            return false;
        }

        if (value is not decimal decimalValue)
        {
            return false;
        }

        if (targetType is not null &&
            !string.Equals(targetType, DotBoxDGenerationNames.ManifestTypes.Record, StringComparison.Ordinal))
        {
            return false;
        }

        model = DecimalRecord(decimalValue, type);
        return true;
    }

    private static DotBoxDExpressionModel Lower(ExpressionSyntax expression, object? value, string type)
        => type switch
        {
            DotBoxDGenerationNames.ManifestTypes.Bool => LowerBool(expression, value),
            DotBoxDGenerationNames.ManifestTypes.Int => LowerInt32(expression, value),
            DotBoxDGenerationNames.ManifestTypes.Long => LowerInt64(expression, value),
            DotBoxDGenerationNames.ManifestTypes.Double => LowerFloat64(expression, value),
            DotBoxDGenerationNames.ManifestTypes.String => LowerString(expression, value),
            _ => UnsupportedConstant(expression),
        };

    private static DotBoxDExpressionModel LowerBool(ExpressionSyntax expression, object? value)
        => value is bool boolean ? Bool(boolean) : UnsupportedConstant(expression);

    private static DotBoxDExpressionModel LowerInt32(ExpressionSyntax expression, object? value)
        => value is int number ? Int32(number) : UnsupportedConstant(expression);

    private static DotBoxDExpressionModel LowerInt64(ExpressionSyntax expression, object? value)
        => value switch
        {
            int number => Int64(number),
            long number => Int64(number),
            _ => UnsupportedConstant(expression),
        };

    private static DotBoxDExpressionModel LowerFloat64(ExpressionSyntax expression, object? value)
        => value switch
        {
            int number => Float64(number),
            long number => Float64(number),
            float number when IsFinite(number) => Float64(number),
            double number when IsFinite(number) => Float64(number),
            _ => UnsupportedConstant(expression),
        };

    private static DotBoxDExpressionModel LowerString(ExpressionSyntax expression, object? value)
        => value is string text ? String(text) : UnsupportedConstant(expression);

    private static DotBoxDExpressionModel UnsupportedConstant(ExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin constant expression '{expression}'.");

    private static DotBoxDExpressionModel? TryLowerDefaultValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? targetType)
    {
        if (!expression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
            expression is not DefaultExpressionSyntax)
        {
            return null;
        }

        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type is null)
        {
            return null;
        }

        var manifestTag = SandboxTypeSourceEmitter.ManifestTag(type);
        if (targetType is not null && !string.Equals(targetType, manifestTag, StringComparison.Ordinal))
        {
            return null;
        }

        return LowerDefault(type, manifestTag);
    }

    private static DotBoxDExpressionModel? LowerDefault(ITypeSymbol type, string manifestTag)
    {
        if (string.Equals(manifestTag, DotBoxDGenerationNames.ManifestTypes.Guid, StringComparison.Ordinal))
        {
            return DotBoxDRpcTypeMapper.IsGuid(type) ? GuidDefault() : null;
        }

        if (string.Equals(manifestTag, DotBoxDGenerationNames.ManifestTypes.Record, StringComparison.Ordinal))
        {
            return LowerRecordDefault(type);
        }

        if (string.Equals(manifestTag, DotBoxDGenerationNames.ManifestTypes.Int, StringComparison.Ordinal))
        {
            return DotBoxDRpcTypeMapper.IsDateOnlyWireType(type) ? Int32(0) : null;
        }

        return LowerLongDefault(type, manifestTag);
    }

    private static DotBoxDExpressionModel? LowerRecordDefault(ITypeSymbol type)
    {
        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            return DateTimeRecordDefault(type);
        }

        return DotBoxDRpcTypeMapper.IsDecimalWireType(type) ? DecimalRecord(default, type) : null;
    }

    private static DotBoxDExpressionModel? LowerLongDefault(ITypeSymbol type, string manifestTag)
    {
        if (!string.Equals(manifestTag, DotBoxDGenerationNames.ManifestTypes.Long, StringComparison.Ordinal))
        {
            return null;
        }

        return DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type) || DotBoxDRpcTypeMapper.IsTimeSpanWireType(type)
            ? Int64(0)
            : null;
    }

    private static DotBoxDExpressionModel DateTimeRecordDefault(ITypeSymbol type)
        => new(
            DotBoxDRecordCreationExpressionLowerer.RecordNew(
                [
                    $"{DotBoxDGenerationNames.Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
                    $"{DotBoxDGenerationNames.Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})"
                ],
            SandboxTypeSourceEmitter.TryEmit(type) ?? throw new NotSupportedException()),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);

    private static DotBoxDExpressionModel DecimalRecord(decimal value, ITypeSymbol type)
        => new(
            DotBoxDDecimalWireSource.RecordSource(type, value),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);

    private static DotBoxDExpressionModel Bool(bool value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.Bool}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            false);

    private static DotBoxDExpressionModel Int32(int value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Int,
            false);

    private static DotBoxDExpressionModel Int64(long value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Long,
            false);

    // A ulong-backed enum value above long.MaxValue overflows a range-checked Convert.ToInt64; reinterpret its
    // bits instead so the value carries losslessly (the decoder casts the I64 back to the enum, also unchecked).
    private static long EnumConstantToInt64(object? value, INamedTypeSymbol enumType)
        => enumType.EnumUnderlyingType?.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_UInt64
            ? unchecked((long)Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture))
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

    private static DotBoxDExpressionModel Float64(double value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Double,
            false);

    private static DotBoxDExpressionModel String(string value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.String,
            true);

    private static DotBoxDExpressionModel GuidDefault()
        => new(
            $"new {DotBoxDGenerationNames.TypeNames.GlobalLiteralExpression}({DotBoxDGenerationNames.TypeNames.GlobalSandboxValue}.FromGuid(global::System.Guid.Empty), Span)",
            DotBoxDGenerationNames.ManifestTypes.Guid,
            false);

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
