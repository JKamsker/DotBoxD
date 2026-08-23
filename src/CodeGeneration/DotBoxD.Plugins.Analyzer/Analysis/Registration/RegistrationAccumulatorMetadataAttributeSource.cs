namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;

internal static class RegistrationAccumulatorMetadataAttributeSource
{
    public static EquatableArray<string> TypeAttributes(INamedTypeSymbol type, Compilation compilation)
        => AttributeLines(type, compilation);

    public static EquatableArray<string> MethodAttributes(IMethodSymbol method, Compilation compilation)
        => AttributeLines(method, compilation);

    public static bool RequiresExperimentalWarningSuppression(params ISymbol[] symbols)
        => symbols.SelectMany(static symbol => symbol.GetAttributes()).Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() ==
                "System.Diagnostics.CodeAnalysis.ExperimentalAttribute" &&
            attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0].Value is string diagnosticId &&
            IsPragmaWarningIdentifier(diagnosticId));

    private static EquatableArray<string> AttributeLines(ISymbol symbol, Compilation compilation)
    {
        var lines = new List<string>();
        lines.AddRange(RegistrationObsoleteAttributeSource.Attributes(symbol, compilation));

        var experimentalAttribute = compilation.GetTypeByMetadataName(
            "System.Diagnostics.CodeAnalysis.ExperimentalAttribute");

        foreach (var attribute in symbol.GetAttributes())
        {
            if (ExperimentalAttribute(attribute, experimentalAttribute) is { } source)
            {
                lines.Add(source);
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return new EquatableArray<string>(lines.ToArray());
    }

    private static bool IsPragmaWarningIdentifier(string value)
        => value.Length > 0 &&
           value.All(static ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private static string? ExperimentalAttribute(AttributeData attribute, INamedTypeSymbol? experimentalAttribute)
    {
        if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, experimentalAttribute) ||
            attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string diagnosticId ||
            string.IsNullOrWhiteSpace(diagnosticId))
        {
            return null;
        }

        var arguments = new List<string> { LiteralReader.StringLiteral(diagnosticId) };
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Value.Value is not (null or string))
            {
                return null;
            }

            arguments.Add(argument.Key + " = " + LiteralReader.ObjectLiteral(argument.Value.Value));
        }

        return "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(" +
            string.Join(", ", arguments) +
            ")]";
    }
}
