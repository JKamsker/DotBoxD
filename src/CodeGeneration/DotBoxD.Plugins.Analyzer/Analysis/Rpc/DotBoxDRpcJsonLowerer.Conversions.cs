using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    private string ApplyNumericConversion(ExpressionSyntax expression, string lowered)
    {
        var type = _model.GetTypeInfo(expression, _cancellationToken);
        if (type.Type is null ||
            type.ConvertedType is null ||
            SymbolEqualityComparer.Default.Equals(type.Type, type.ConvertedType))
        {
            return lowered;
        }

        if (type.Type.SpecialType == SpecialType.System_Int32 &&
            type.ConvertedType.SpecialType == SpecialType.System_Int64)
        {
            return Call("numeric.toI64", null, lowered);
        }

        if (type.Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 &&
            type.ConvertedType.SpecialType == SpecialType.System_Double)
        {
            return Call("numeric.toF64", null, lowered);
        }

        return lowered;
    }
}
