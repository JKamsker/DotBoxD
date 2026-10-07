using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcOperatorSemanticsValidator
{
    public static void Validate(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken)
    {
        var left = model.GetTypeInfo(binary.Left, cancellationToken).Type;
        var right = model.GetTypeInfo(binary.Right, cancellationToken).Type;
        ValidateWireOperand(left);
        ValidateWireOperand(right);
        if (binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression)
        {
            ValidateReferenceOperand(left);
            ValidateReferenceOperand(right);
        }

        if (model.GetSymbolInfo(binary, cancellationToken).Symbol is
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method &&
            !IsSupportedBinaryOperator(binary.Kind(), method.ContainingType))
        {
            throw new NotSupportedException(
                "This user-defined operator is unsupported; operate on supported scalar fields explicitly or use a remote handler.");
        }
    }

    public static void ValidateCompound(AssignmentExpressionSyntax assignment, SemanticModel model, CancellationToken cancellationToken)
    {
        ValidateWireOperand(model.GetTypeInfo(assignment.Left, cancellationToken).Type);
        ValidateWireOperand(model.GetTypeInfo(assignment.Right, cancellationToken).Type);
        if (model.GetSymbolInfo(assignment, cancellationToken).Symbol is
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method &&
            !IsSupportedBinaryOperator(assignment.Kind(), method.ContainingType))
        {
            throw new NotSupportedException(
                "This user-defined compound operator is unsupported; operate on supported scalar fields explicitly or use a remote handler.");
        }
    }

    public static void ValidateUnary(PrefixUnaryExpressionSyntax unary, SemanticModel model, CancellationToken cancellationToken)
    {
        ValidateWireOperand(model.GetTypeInfo(unary.Operand, cancellationToken).Type);
        if (model.GetSymbolInfo(unary, cancellationToken).Symbol is
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method &&
            method.ContainingType.SpecialType == SpecialType.None)
        {
            throw new NotSupportedException("This user-defined unary operator is unsupported; operate on supported scalar fields explicitly or use a remote handler.");
        }
    }

    private static void ValidateReferenceOperand(ITypeSymbol? type)
    {
        if (type is { IsReferenceType: true, SpecialType: not SpecialType.System_String })
        {
            throw new NotSupportedException(
                "Reference equality is unsupported because kernel collections and records have value semantics; compare supported scalar fields explicitly or use a remote handler.");
        }

    }

    private static void ValidateWireOperand(ITypeSymbol? type)
    {
        if (type is not null && DotBoxDRpcTypeMapper.IsFirstClassFrameworkWireStruct(type) &&
            !IsScalarFrameworkValue(type))
        {
            throw new NotSupportedException(
                $"Operators on '{type.ToDisplayString()}' are unsupported because its wire representation does not preserve CLR operator semantics; operate on supported scalar fields explicitly or use a remote handler.");
        }
    }

    private static bool IsSupportedBinaryOperator(SyntaxKind kind, ITypeSymbol type)
        => type.SpecialType is not (SpecialType.None or SpecialType.System_Decimal) ||
           IsComparison(kind) && IsScalarFrameworkValue(type);

    private static bool IsComparison(SyntaxKind kind)
        => kind is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression
            or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression;

    private static bool IsScalarFrameworkValue(ITypeSymbol type)
        => DotBoxDRpcTypeMapper.IsGuid(type) || DotBoxDRpcTypeMapper.IsDateOnlyWireType(type) ||
           DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type) || DotBoxDRpcTypeMapper.IsTimeSpanWireType(type);
}
