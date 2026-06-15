using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Infrastructure;

internal static class DiagnosticLocationFactory
{
    public static DiagnosticLocation FromSymbol(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                return DiagnosticLocation.FromLocation(location);
            }
        }

        return default;
    }
}
