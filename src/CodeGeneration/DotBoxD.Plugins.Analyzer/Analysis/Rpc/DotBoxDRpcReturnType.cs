using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class DotBoxDRpcReturnType
{
    public static string JsonType(ITypeSymbol type)
    {
        var payloadType = PayloadType(type);
        return payloadType is null ? "\"Unit\"" : DotBoxDRpcTypeMapper.JsonType(payloadType);
    }

    public static ITypeSymbol? PayloadType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Void)
        {
            return null;
        }

        if (IsTaskLike(type, out var taskLike))
        {
            return taskLike is { IsGenericType: true, TypeArguments.Length: 1 }
                ? taskLike.TypeArguments[0]
                : null;
        }

        return type;
    }

    public static bool IsTaskLike(ITypeSymbol type)
        => IsTaskLike(type, out _);

    private static bool IsTaskLike(ITypeSymbol type, out INamedTypeSymbol? taskLike)
    {
        if (type is INamedTypeSymbol
            {
                Name: "Task" or "ValueTask",
                ContainingNamespace: { } ns
            } named &&
            string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
        {
            taskLike = named;
            return true;
        }

        taskLike = null;
        return false;
    }
}
