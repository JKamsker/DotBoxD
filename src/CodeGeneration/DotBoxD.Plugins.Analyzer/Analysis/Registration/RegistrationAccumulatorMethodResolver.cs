namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using Microsoft.CodeAnalysis;

internal static class RegistrationAccumulatorMethodResolver
{
    public static IMethodSymbol[] FindInstanceMethods(INamedTypeSymbol type, string methodName)
    {
        var methods = new List<IMethodSymbol>();
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (candidate.MethodKind != MethodKind.Ordinary || candidate.IsStatic ||
                    methods.Any(method => Overrides(method, candidate)))
                {
                    continue;
                }

                methods.Add(candidate);
            }
        }

        return methods.ToArray();
    }

    private static bool Overrides(IMethodSymbol method, IMethodSymbol candidate)
    {
        for (var overridden = method.OverriddenMethod;
             overridden is not null;
             overridden = overridden.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(overridden, candidate))
            {
                return true;
            }
        }

        return false;
    }
}
