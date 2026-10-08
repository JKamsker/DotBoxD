using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    public string LowerExpressionBody(
        ExpressionSyntax expression,
        bool returnsVoid,
        ITypeSymbol? returnValueType)
    {
        _assignmentOverride = null;
        _expressionOverride = null;
        _returnRecordFields = [];
        _returnRecordType = null;
        try
        {
            ReserveUserNames(expression);
            var parts = new List<string>();
            if (returnsVoid)
            {
                RpcJsonExpressionStatementLowerer.LowerExpressionStatement(this, expression, parts);
            }
            else
            {
                if (returnValueType is not null)
                {
                    RejectUserDefinedConversion(expression, returnValueType,
                        $"Server extension return expression '{expression}'");
                }
                var value = LowerExpressionWithPrelude(expression, parts);
                if (returnValueType is not null)
                {
                    value = ApplyRequiredReturnConversion(expression, returnValueType, value);
                }

                parts.Add(Obj(
                    ("op", Str("return")),
                    ("value", value)));
            }

            return "[" + string.Join(",", parts) + "]";
        }
        finally
        {
            _assignmentOverride = null;
            _expressionOverride = null;
            _returnRecordFields = [];
            _returnRecordType = null;
        }
    }
}
