using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class DotBoxDRpcReturnType
{
    public static ITypeSymbol? PayloadType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Void)
        {
            return null;
        }

        if (type is INamedTypeSymbol
            {
                Name: "Task" or "ValueTask",
                ContainingNamespace: { } ns
            } taskLike &&
            string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
        {
            return taskLike is { IsGenericType: true, TypeArguments.Length: 1 }
                ? taskLike.TypeArguments[0]
                : null;
        }

        return type;
    }
}
