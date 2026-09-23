using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class ExperimentalAttributeSource
{
    private const string ExperimentalAttributeName = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";
    private const string RequiresDynamicCodeAttributeName =
        "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute";
    private const string RequiresUnreferencedCodeAttributeName =
        "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute";

    public static string FromTypes(params ITypeSymbol?[] types)
    {
        var diagnosticIds = new SortedSet<string>(StringComparer.Ordinal);
        var codeRequirementAttributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            Collect(type, diagnosticIds, codeRequirementAttributes);
        }

        var source = string.Empty;
        if (diagnosticIds.Count > 0)
        {
            source = "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(" +
                     LiteralReader.StringLiteral(diagnosticIds.Min!) +
                     ")]\n";
        }

        return source + string.Concat(codeRequirementAttributes.Values);
    }

    private static void Collect(
        ITypeSymbol? type,
        ISet<string> diagnosticIds,
        IDictionary<string, string> codeRequirementAttributes)
    {
        switch (type)
        {
            case null:
                return;
            case IArrayTypeSymbol array:
                Collect(array.ElementType, diagnosticIds, codeRequirementAttributes);
                return;
            case INamedTypeSymbol named:
                CollectNamed(named, diagnosticIds, codeRequirementAttributes);
                return;
        }
    }

    private static void CollectNamed(
        INamedTypeSymbol named,
        ISet<string> diagnosticIds,
        IDictionary<string, string> codeRequirementAttributes)
    {
        foreach (var attribute in named.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ExperimentalAttributeName &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string diagnosticId)
            {
                diagnosticIds.Add(diagnosticId);
            }

            CollectCodeRequirementAttribute(attribute, codeRequirementAttributes);
        }

        foreach (var argument in named.TypeArguments)
        {
            Collect(argument, diagnosticIds, codeRequirementAttributes);
        }
    }

    private static void CollectCodeRequirementAttribute(
        AttributeData attribute,
        IDictionary<string, string> codeRequirementAttributes)
    {
        var attributeClass = attribute.AttributeClass;
        var name = attributeClass?.ToDisplayString();
        if (name is not RequiresDynamicCodeAttributeName and not RequiresUnreferencedCodeAttributeName ||
            attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string message)
        {
            return;
        }

        var url = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Url").Value.Value as string;
        var urlAssignment = url is null
            ? string.Empty
            : ", Url = " + LiteralReader.StringLiteral(url);
        if (!codeRequirementAttributes.ContainsKey(attributeClass!.Name))
        {
            codeRequirementAttributes.Add(
                attributeClass.Name,
                "[global::System.Diagnostics.CodeAnalysis." + attributeClass.Name + "(" +
                LiteralReader.StringLiteral(message) + urlAssignment + ")]\n");
        }
    }
}
