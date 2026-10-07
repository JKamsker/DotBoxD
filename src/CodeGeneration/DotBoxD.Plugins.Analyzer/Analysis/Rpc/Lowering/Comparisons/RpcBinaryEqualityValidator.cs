using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcBinaryEqualityValidator
{
    public static void Validate(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken)
    {
        if (binary.Kind() is not (SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
        {
            return;
        }

        ValidateOperand(model.GetTypeInfo(binary.Left, cancellationToken).Type);
        ValidateOperand(model.GetTypeInfo(binary.Right, cancellationToken).Type);
        if (model.GetSymbolInfo(binary, cancellationToken).Symbol is
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method &&
            !IsFrameworkValueOperator(method.ContainingType))
        {
            throw new NotSupportedException(
                "User-defined equality operators are unsupported; compare supported scalar fields explicitly or use a remote handler.");
        }
    }

    private static void ValidateOperand(ITypeSymbol? type)
    {
        if (type is { IsReferenceType: true, SpecialType: not SpecialType.System_String })
        {
            throw new NotSupportedException(
                "Reference equality is unsupported because kernel collections and records have value semantics; compare supported scalar fields explicitly or use a remote handler.");
        }

        if (type is not null && DotBoxDRpcTypeMapper.IsFirstClassFrameworkWireStruct(type) &&
            !IsScalarFrameworkValue(type))
        {
            throw new NotSupportedException(
                $"Equality of '{type.ToDisplayString()}' is unsupported because its wire representation does not preserve CLR equality; compare supported scalar fields explicitly or use a remote handler.");
        }
    }

    private static bool IsFrameworkValueOperator(ITypeSymbol type)
        => type.SpecialType is not (SpecialType.None or SpecialType.System_Decimal) || IsScalarFrameworkValue(type);

    private static bool IsScalarFrameworkValue(ITypeSymbol type)
        => DotBoxDRpcTypeMapper.IsGuid(type) || DotBoxDRpcTypeMapper.IsDateOnlyWireType(type) ||
           DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type) || DotBoxDRpcTypeMapper.IsTimeSpanWireType(type);
}
