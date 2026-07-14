using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string LowerCast(CastExpressionSyntax cast)
    {
        var targetType = _model.GetTypeInfo(cast, _cancellationToken).Type
                         ?? _model.GetTypeInfo(cast, _cancellationToken).ConvertedType
                         ?? throw new NotSupportedException(
                             $"Server extension cast '{cast}' could not resolve its target type.");
        return ApplyRequiredNumericConversion(
            cast.Expression,
            targetType,
            LowerExpression(cast.Expression),
            $"Server extension cast '{cast}'");
    }

    internal string ApplyNumericConversion(ExpressionSyntax expression, string lowered)
    {
        var type = _model.GetTypeInfo(expression, _cancellationToken);
        if (type.Type is null ||
            type.ConvertedType is null)
        {
            return lowered;
        }

        return ApplyNumericConversion(type.Type, type.ConvertedType, lowered);
    }

    internal string ApplyNumericConversion(ExpressionSyntax expression, ITypeSymbol targetType, string lowered)
    {
        var sourceType = _model.GetTypeInfo(expression, _cancellationToken).Type;
        return sourceType is null ? lowered : ApplyNumericConversion(sourceType, targetType, lowered);
    }

    internal string ApplyNumericConversion(ITypeSymbol sourceType, ITypeSymbol targetType, string lowered)
        => TryApplyNumericConversion(sourceType, targetType, lowered, out var converted) ? converted : lowered;

    internal string ApplyRequiredReturnConversion(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        string lowered)
        => ApplyRequiredNumericConversion(
            expression,
            targetType,
            lowered,
            $"InvokeAsync return expression '{expression}'");

    internal string ApplyRequiredLocalConversion(
        ExpressionSyntax expression,
        ILocalSymbol local,
        string lowered,
        bool isInferred)
    {
        var targetType = local.Type;
        if (isInferred && targetType.TypeKind == TypeKind.Error)
        {
            var typeInfo = _model.GetTypeInfo(expression, _cancellationToken);
            targetType = EffectiveSourceType(expression, typeInfo)
                         ?? throw UnsupportedConversion($"Server extension local '{local.Name}' initializer");
            _fallbackLocalTypes[local] = targetType;
        }

        return ApplyRequiredNumericConversion(
            expression,
            targetType,
            lowered,
            $"Server extension local '{local.Name}' initializer");
    }

    internal string ApplyRequiredAssignmentConversion(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        string lowered,
        string targetName)
        => ApplyRequiredNumericConversion(
            expression,
            targetType,
            lowered,
            $"Server extension assignment to '{targetName}'");

    private string ApplyRequiredNumericConversion(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        string lowered,
        string description)
    {
        var typeInfo = _model.GetTypeInfo(expression, _cancellationToken);
        if (EffectiveSourceType(expression, typeInfo) is not { } sourceType)
        {
            throw UnsupportedConversion(description);
        }

        var contextualType = typeInfo.ConvertedType is { TypeKind: not TypeKind.Error } convertedType
            ? convertedType
            : sourceType;
        if (!TryEffectiveLoweredType(expression, sourceType, contextualType, out var effectiveType))
        {
            throw UnsupportedConversion(description);
        }

        if (TryApplyNumericConversion(effectiveType, targetType, lowered, out var converted))
        {
            return converted;
        }

        throw UnsupportedConversion(description);
    }

    private ITypeSymbol? EffectiveSourceType(ExpressionSyntax expression, TypeInfo typeInfo)
    {
        if (FallbackLocalType(expression) is { } fallbackLocalType)
        {
            return fallbackLocalType;
        }

        if (typeInfo.Type is { TypeKind: not TypeKind.Error } sourceType)
        {
            return sourceType;
        }

        if (TryServerContextInvocationReturnType(expression) is { } hostReturnType)
        {
            return hostReturnType;
        }

        return typeInfo.ConvertedType is { TypeKind: not TypeKind.Error } convertedType
            ? convertedType
            : null;
    }

    private ITypeSymbol? FallbackLocalType(ExpressionSyntax expression)
    {
        if (expression is not IdentifierNameSyntax identifier ||
            _model.GetSymbolInfo(identifier, _cancellationToken).Symbol is not { } symbol)
        {
            return null;
        }

        return _fallbackLocalTypes.TryGetValue(symbol, out var type) ? type : null;
    }

    private ITypeSymbol? TryServerContextInvocationReturnType(ExpressionSyntax expression)
    {
        if (expression is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax member ||
            !IsServerContextExpression(member.Expression))
        {
            return null;
        }

        var candidates = ServerContextHostBindingCandidates(
            member.Name.Identifier.ValueText,
            invocation.ArgumentList.Arguments);
        return candidates.Count == 1
            ? DotBoxDTypeNameReader.UnwrapTaskLike(candidates[0].Method.ReturnType)
            : null;
    }

    private bool TryEffectiveLoweredType(
        ExpressionSyntax expression,
        ITypeSymbol sourceType,
        ITypeSymbol? convertedType,
        out ITypeSymbol effectiveType)
    {
        effectiveType = sourceType;
        if (convertedType is null || SymbolEqualityComparer.Default.Equals(sourceType, convertedType))
        {
            return true;
        }

        if (IsLoweredEnumConstant(expression, convertedType) ||
            RpcNumericConversionClassifier.IsRepresentableNarrowI32Constant(
                _model,
                expression,
                sourceType,
                convertedType,
                _cancellationToken) ||
            NumericConversion(sourceType, convertedType) is not NumericConversionKind.Unsupported)
        {
            effectiveType = convertedType;
            return true;
        }

        return false;
    }

    private bool IsLoweredEnumConstant(ExpressionSyntax expression, ITypeSymbol convertedType)
        => convertedType.TypeKind == TypeKind.Enum &&
           _model.GetConstantValue(expression, _cancellationToken).HasValue;

    private static NotSupportedException UnsupportedConversion(string description)
        => new($"{description} is not supported because it is not a supported numeric widening conversion.");

    private static bool TryApplyNumericConversion(
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        string lowered,
        out string converted)
    {
        switch (NumericConversion(sourceType, targetType))
        {
            case NumericConversionKind.Identity:
            case NumericConversionKind.SameWire:
                converted = lowered;
                return true;
            case NumericConversionKind.ToI64:
                converted = Call("numeric.toI64", null, lowered);
                return true;
            case NumericConversionKind.ToF64:
                converted = Call("numeric.toF64", null, lowered);
                return true;
            default:
                converted = lowered;
                return false;
        }
    }

    private static NumericConversionKind NumericConversion(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            return NumericConversionKind.Identity;
        }

        if (RpcNumericConversionClassifier.IsImplicitI32WireConversion(sourceType, targetType) ||
            (sourceType.SpecialType == SpecialType.System_Single && targetType.SpecialType == SpecialType.System_Double))
        {
            return NumericConversionKind.SameWire;
        }

        if (IsI32WireIntegral(sourceType) && targetType.SpecialType == SpecialType.System_Int64)
        {
            return NumericConversionKind.ToI64;
        }

        return CanWidenToF64(sourceType) && IsFloatingPoint(targetType)
            ? NumericConversionKind.ToF64
            : NumericConversionKind.Unsupported;
    }

    private static bool IsI32WireIntegral(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Byte or
            SpecialType.System_Char or
            SpecialType.System_Int16 or
            SpecialType.System_Int32 or
            SpecialType.System_SByte or
            SpecialType.System_UInt16;

    private static bool CanWidenToF64(ITypeSymbol type)
        => IsI32WireIntegral(type) || type.SpecialType == SpecialType.System_Int64;

    private static bool IsFloatingPoint(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Double or SpecialType.System_Single;

    private enum NumericConversionKind
    {
        Unsupported,
        Identity,
        SameWire,
        ToI64,
        ToF64
    }
}
