using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class ExperimentalAttributeSource
{
    private const string ExperimentalAttributeName = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";
    private static readonly HashSet<string> PlatformCompatibilityAttributeNames =
    [
        "System.Runtime.Versioning.SupportedOSPlatformAttribute",
        "System.Runtime.Versioning.UnsupportedOSPlatformAttribute",
        "System.Runtime.Versioning.ObsoletedOSPlatformAttribute",
    ];

    public static string FromTypes(params ITypeSymbol?[] types)
    {
        var diagnosticIds = new SortedSet<string>(StringComparer.Ordinal);
        var platformAttributes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            Collect(type, diagnosticIds, platformAttributes);
        }

        var source = string.Empty;
        if (diagnosticIds.Count > 0)
        {
            source = "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(" +
                LiteralReader.StringLiteral(diagnosticIds.Min!) +
                ")]\n";
        }

        return source + string.Concat(platformAttributes);
    }

    private static void Collect(
        ITypeSymbol? type,
        ISet<string> diagnosticIds,
        ISet<string> platformAttributes)
    {
        switch (type)
        {
            case null:
                return;
            case IArrayTypeSymbol array:
                Collect(array.ElementType, diagnosticIds, platformAttributes);
                return;
            case INamedTypeSymbol named:
                CollectNamed(named, diagnosticIds, platformAttributes);
                return;
        }
    }

    private static void CollectNamed(
        INamedTypeSymbol named,
        ISet<string> diagnosticIds,
        ISet<string> platformAttributes)
    {
        foreach (var attribute in named.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ExperimentalAttributeName &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string diagnosticId)
            {
                diagnosticIds.Add(diagnosticId);
            }

            if (PlatformCompatibilityAttributeSource(attribute) is { } source)
            {
                platformAttributes.Add(source);
            }
        }

        foreach (var argument in named.TypeArguments)
        {
            Collect(argument, diagnosticIds, platformAttributes);
        }
    }

    private static string? PlatformCompatibilityAttributeSource(AttributeData attribute)
    {
        var attributeType = attribute.AttributeClass;
        if (attributeType is null ||
            !PlatformCompatibilityAttributeNames.Contains(attributeType.ToDisplayString()) ||
            attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments.Any(argument => argument.Value is not string))
        {
            return null;
        }

        var arguments = string.Join(
            ", ",
            attribute.ConstructorArguments.Select(argument => LiteralReader.StringLiteral((string)argument.Value!)));
        return "[" + attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "(" + arguments + ")]\n";
    }
}
