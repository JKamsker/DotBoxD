using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static class CodeRequirementAttributeSourceFactory
{
    public static Dictionary<INamedTypeSymbol, string> CreateMap(Compilation compilation)
    {
        var attributes = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
        Add(attributes, compilation, "System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute");
        Add(attributes, compilation, "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute");
        Add(attributes, compilation, "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute");
        Add(attributes, compilation, "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute");
        return attributes;
    }

    public static string? Create(
        AttributeData attribute,
        Dictionary<INamedTypeSymbol, string> codeRequirementAttributes)
    {
        if (attribute.AttributeClass is not { } attributeClass ||
            !codeRequirementAttributes.TryGetValue(attributeClass, out var attributeName))
        {
            return null;
        }

        var arguments = new List<string>();
        if (attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0].Value is string message)
        {
            arguments.Add(LiteralReader.StringLiteral(message));
        }
        else if (attribute.ConstructorArguments.Length != 0 ||
                 !AllowsParameterlessConstructor(attributeName))
        {
            return null;
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument is { Key: "Url", Value.Value: string url })
            {
                arguments.Add("Url = " + LiteralReader.StringLiteral(url));
            }
        }

        return arguments.Count == 0
            ? "[" + attributeName + "]"
            : "[" + attributeName + "(" + string.Join(", ", arguments) + ")]";
    }

    private static void Add(
        Dictionary<INamedTypeSymbol, string> attributes,
        Compilation compilation,
        string metadataName)
    {
        if (compilation.GetTypeByMetadataName(metadataName) is { } attribute)
        {
            attributes.Add(attribute, "global::" + metadataName);
        }
    }

    private static bool AllowsParameterlessConstructor(string attributeName)
        => attributeName is "global::System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute"
            or "global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute";
}
