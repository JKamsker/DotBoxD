using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    internal void ValidateBinarySingleSemantics(BinaryExpressionSyntax binary)
    {
        if (_model.GetConstantValue(binary, _cancellationToken).HasValue)
        {
            return;
        }

        var description = $"Server extension binary operator '{binary.OperatorToken.Text}'";
        RejectRuntimeSingleResult(
            _model.GetTypeInfo(binary, _cancellationToken).Type,
            description);
        ValidateBinarySingleOperand(binary.Left, description);
        ValidateBinarySingleOperand(binary.Right, description);
    }

    internal static void RejectRuntimeSingleResult(ITypeSymbol? resultType, string description)
    {
        if (resultType?.SpecialType == SpecialType.System_Single)
        {
            throw UnsupportedRuntimeSingleRounding(description);
        }
    }

    private void ValidateBinarySingleOperand(ExpressionSyntax operand, string description)
    {
        var type = _model.GetTypeInfo(operand, _cancellationToken);
        if (type.ConvertedType?.SpecialType == SpecialType.System_Single &&
            type.Type?.SpecialType != SpecialType.System_Single)
        {
            throw UnsupportedRuntimeSingleRounding(
                $"{description} operand '{operand}'");
        }
    }

    private static NotSupportedException UnsupportedRuntimeSingleRounding(string description)
        => new(
            $"{description} requires runtime System.Single rounding, which server-extension F64 arithmetic cannot preserve.");
}
