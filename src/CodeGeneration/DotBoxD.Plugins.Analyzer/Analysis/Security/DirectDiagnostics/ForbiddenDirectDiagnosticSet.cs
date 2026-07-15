using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed class ForbiddenDirectDiagnosticSet
{
    private readonly ConcurrentDictionary<ISymbol, ConcurrentDictionary<string, byte>> _diagnostics =
        new(SymbolEqualityComparer.Default);

    public bool TryAdd(ISymbol method, string displayName)
    {
        var methodDiagnostics = _diagnostics.GetOrAdd(
            method,
            static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        return methodDiagnostics.TryAdd(displayName, 0);
    }
}
