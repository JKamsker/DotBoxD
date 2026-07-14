using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenGraphAnalysis
{
    public static bool IsStaticSourceMember(ISymbol symbol)
        => symbol.DeclaringSyntaxReferences.Length != 0 &&
            symbol is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true };

    public static bool IsReachableSourceMethod(IMethodSymbol method)
        => method.DeclaringSyntaxReferences.Length != 0 ||
            method is
            {
                MethodKind: MethodKind.Constructor,
                IsStatic: false,
                ContainingType.DeclaringSyntaxReferences.Length: > 0
            };

    public static Dictionary<ISymbol, string> Propagate(
        ConcurrentDictionary<ISymbol, string> forbidden,
        IEnumerable<(ISymbol Caller, ISymbol Target)> edges)
    {
        var tainted = new Dictionary<ISymbol, string>(forbidden, SymbolEqualityComparer.Default);
        var callersByTarget = new Dictionary<ISymbol, List<ISymbol>>(SymbolEqualityComparer.Default);
        foreach (var (caller, target) in edges)
        {
            if (!callersByTarget.TryGetValue(target, out var callers))
            {
                callers = [];
                callersByTarget.Add(target, callers);
            }

            callers.Add(caller);
        }

        var pending = new Queue<ISymbol>(tainted.Keys);
        while (pending.Count > 0)
        {
            var target = pending.Dequeue();
            if (!tainted.TryGetValue(target, out var displayName) ||
                !callersByTarget.TryGetValue(target, out var callers))
            {
                continue;
            }

            foreach (var caller in callers)
            {
                if (!tainted.ContainsKey(caller))
                {
                    tainted.Add(caller, displayName);
                    pending.Enqueue(caller);
                }
            }
        }

        return tainted;
    }

    public static ITypeSymbol? FirstForbiddenSignatureType(IMethodSymbol target)
    {
        var forbidden = PluginAnalyzer.FirstForbiddenHostApi(target.ReturnType);
        foreach (var parameter in target.Parameters)
        {
            forbidden ??= PluginAnalyzer.FirstForbiddenHostApi(parameter.Type);
        }

        return forbidden;
    }
}
