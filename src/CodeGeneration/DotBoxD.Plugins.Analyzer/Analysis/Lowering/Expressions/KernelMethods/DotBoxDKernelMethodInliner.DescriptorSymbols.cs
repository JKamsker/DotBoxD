using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static bool GeneratedContextMatches(
        INamedTypeSymbol serverType,
        INamedTypeSymbol contextType,
        Compilation compilation)
    {
        foreach (var attribute in serverType.GetAttributes())
        {
            if (!IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.GeneratePluginServerAttribute))
            {
                continue;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "Context", StringComparison.Ordinal) &&
                    argument.Value.Value is ITypeSymbol declaredContext &&
                    SymbolEqualityComparer.Default.Equals(declaredContext, contextType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasAttribute(INamedTypeSymbol type, string metadataName, Compilation compilation)
        => type.GetAttributes().Any(attribute => IsDotBoxDAttribute(attribute, compilation, metadataName));

    private static IEnumerable<INamedTypeSymbol> TypesInNamespace(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in NestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in TypesInNamespace(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in NestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }
}
