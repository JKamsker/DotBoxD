using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private static string BuildReturnTypeJson(ITypeSymbol returnType, IReadOnlyList<InvokeAsyncSyncOut> syncOuts)
    {
        if (syncOuts.Count == 0)
        {
            return DotBoxDRpcReturnType.JsonType(returnType);
        }

        var fields = new string[1 + syncOuts.Count];
        fields[0] = DotBoxDRpcReturnType.JsonType(returnType);
        for (var i = 0; i < syncOuts.Count; i++)
        {
            fields[i + 1] = DotBoxDRpcTypeMapper.JsonType(syncOuts[i].Type);
        }

        return "{\"name\":\"Record\",\"arguments\":[" + string.Join(",", fields) + "]}";
    }
}

internal sealed record InvokeAsyncCaptureParameter(string Name, INamedTypeSymbol Type);

internal sealed record InvokeAsyncSyncOut(
    string TargetName,
    ITypeSymbol Type,
    string LocalName,
    ExpressionSyntax? Initializer);
