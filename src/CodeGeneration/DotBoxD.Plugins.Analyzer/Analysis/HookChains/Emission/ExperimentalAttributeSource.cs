using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class ExperimentalAttributeSource
{
    private const string ExperimentalAttributeName = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";

    public static string FromTypes(params ITypeSymbol?[] types)
    {
        var diagnosticIds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            Collect(type, diagnosticIds);
        }

        return diagnosticIds.Count == 0
            ? string.Empty
            : "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(" +
              LiteralReader.StringLiteral(diagnosticIds.Min!) +
              ")]\n";
    }

    private static void Collect(ITypeSymbol? type, ISet<string> diagnosticIds)
    {
        switch (type)
        {
            case null:
                return;
            case IArrayTypeSymbol array:
                Collect(array.ElementType, diagnosticIds);
                return;
            case INamedTypeSymbol named:
                CollectNamed(named, diagnosticIds);
                return;
        }
    }

    private static void CollectNamed(INamedTypeSymbol named, ISet<string> diagnosticIds)
    {
        foreach (var attribute in named.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ExperimentalAttributeName &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string diagnosticId)
            {
                diagnosticIds.Add(diagnosticId);
            }
        }

        foreach (var argument in named.TypeArguments)
        {
            Collect(argument, diagnosticIds);
        }
    }
}
