using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Helpers = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.Helpers;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDNullableScalarExpressionLowerer
{
    private static readonly ZeroSourceResolver[] ZeroSourceResolvers =
    [
        TrySpecialZeroSource,
        TryFrameworkZeroSource,
        TryEnumZeroSource,
    ];

    private delegate string? ZeroSourceResolver(ITypeSymbol underlying);

    public static bool TryLower(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression,
        out DotBoxDExpressionModel lowered)
    {
        if (!DotBoxDNullableScalarType.TryGetSupportedUnderlying(targetType, out var underlying))
        {
            lowered = null!;
            return false;
        }

        if (IsNullLike(expression, context))
        {
            lowered = Null(targetType, underlying);
            return true;
        }

        if (DotBoxDConstantExpressionLowerer.TryLower(
                expression,
                context.SemanticModel,
                context.CancellationToken,
                SandboxTypeSourceEmitter.ManifestTag(underlying)) is { } constant)
        {
            lowered = Present(targetType, underlying, constant);
            return true;
        }

        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        if (typeInfo.Type is { } expressionType &&
            DotBoxDNullableScalarType.TryGetSupportedUnderlying(expressionType, out var expressionUnderlying) &&
            SymbolEqualityComparer.Default.Equals(expressionUnderlying, underlying))
        {
            lowered = lowerExpression(expression);
            RequireTag(lowered, DotBoxDGenerationNames.ManifestTypes.Record);
            return true;
        }

        var value = lowerExpression(expression);
        RequireTag(value, SandboxTypeSourceEmitter.ManifestTag(underlying));
        lowered = Present(targetType, underlying, value);
        return true;
    }

    public static string NullSource(ITypeSymbol nullableType)
    {
        if (!DotBoxDNullableScalarType.TryGetSupportedUnderlying(nullableType, out var underlying))
        {
            throw new NotSupportedException();
        }

        return Null(nullableType, underlying).Source;
    }

    public static string PresentSource(ITypeSymbol nullableType, DotBoxDExpressionModel value)
    {
        if (!DotBoxDNullableScalarType.TryGetSupportedUnderlying(nullableType, out var underlying))
        {
            throw new NotSupportedException();
        }

        RequireTag(value, SandboxTypeSourceEmitter.ManifestTag(underlying));
        return Present(nullableType, underlying, value).Source;
    }

    private static DotBoxDExpressionModel Null(ITypeSymbol nullableType, ITypeSymbol underlying)
        => new(RecordSource(nullableType, BoolSource(value: false), ZeroSource(underlying)), DotBoxDGenerationNames.ManifestTypes.Record, true);

    private static DotBoxDExpressionModel Present(
        ITypeSymbol nullableType,
        ITypeSymbol underlying,
        DotBoxDExpressionModel value)
        => new(
            RecordSource(nullableType, BoolSource(value: true), value.Source),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);

    private static string RecordSource(ITypeSymbol nullableType, string hasValue, string value)
        => DotBoxDRecordCreationExpressionLowerer.RecordNew(
            [hasValue, value],
            SandboxTypeSourceEmitter.TryEmit(nullableType) ?? throw new NotSupportedException());

    private static bool IsNullLike(ExpressionSyntax expression, DotBoxDExpressionLoweringContext context)
    {
        if (expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
            IsNullableDefaultExpression(expression, context))
        {
            return true;
        }

        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        return constant.HasValue && constant.Value is null;
    }

    private static bool IsNullableDefaultExpression(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context)
    {
        if (!expression.IsKind(SyntaxKind.DefaultExpression))
        {
            return false;
        }

        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        return type is not null && DotBoxDNullableScalarType.IsNullableValueType(type);
    }

    private static string ZeroSource(ITypeSymbol underlying)
    {
        foreach (var resolver in ZeroSourceResolvers)
        {
            if (resolver(underlying) is { } source)
            {
                return source;
            }
        }

        throw new NotSupportedException();
    }

    private static string? TrySpecialZeroSource(ITypeSymbol underlying)
        => underlying.SpecialType switch
        {
            SpecialType.System_Boolean => BoolSource(value: false),
            SpecialType.System_Int32 => $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
            SpecialType.System_Int64 => $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
            SpecialType.System_Double or SpecialType.System_Single =>
                $"{Helpers.F64}({DotBoxDGenerationNames.CSharpLiterals.DoubleDefault})",
            _ => null,
        };

    private static string? TryFrameworkZeroSource(ITypeSymbol underlying)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(underlying))
        {
            return $"new {TypeNames.GlobalLiteralExpression}({TypeNames.GlobalSandboxValue}.FromGuid(global::System.Guid.Empty), Span)";
        }

        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(underlying))
        {
            return DateTimeZeroSource(underlying);
        }

        if (DotBoxDRpcTypeMapper.IsDecimalWireType(underlying))
        {
            return DecimalZeroSource(underlying);
        }

        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(underlying))
        {
            return $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})";
        }

        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(underlying))
        {
            return $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})";
        }

        if (DotBoxDRpcTypeMapper.IsTimeSpanWireType(underlying))
        {
            return $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})";
        }

        return DotBoxDRpcTypeMapper.IsCancellationTokenWireType(underlying) ? BoolSource(value: false) : null;
    }

    private static string? TryEnumZeroSource(ITypeSymbol underlying)
        => underlying.TypeKind == TypeKind.Enum && underlying is INamedTypeSymbol enumType
            ? DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
                ? $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})"
                : $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})"
            : null;

    private static string DateTimeZeroSource(ITypeSymbol underlying)
        => DotBoxDRecordCreationExpressionLowerer.RecordNew(
            [
                $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
                $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})"
            ],
            SandboxTypeSourceEmitter.TryEmit(underlying) ?? throw new NotSupportedException());

    private static string DecimalZeroSource(ITypeSymbol underlying)
        => DotBoxDDecimalWireSource.RecordSource(underlying, default);

    private static string BoolSource(bool value)
        => $"{Helpers.Bool}({(value ? DotBoxDGenerationNames.CSharpLiterals.True : DotBoxDGenerationNames.CSharpLiterals.False)})";

    private static void RequireTag(DotBoxDExpressionModel expression, string expected)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal))
        {
            throw new NotSupportedException();
        }
    }
}
