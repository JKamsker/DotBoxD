namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;

internal static class RegistrationAccumulatorMetadataAttributeSource
{
    public static EquatableArray<string> TypeAttributes(INamedTypeSymbol type)
        => AttributeLines(type);

    public static EquatableArray<string> MethodAttributes(IMethodSymbol method)
        => AttributeLines(method);

    private static EquatableArray<string> AttributeLines(ISymbol symbol)
    {
        var lines = new List<string>();
        lines.AddRange(RegistrationObsoleteAttributeSource.Attributes(symbol));

        foreach (var attribute in symbol.GetAttributes())
        {
            if (ExperimentalAttribute(attribute) is { } source)
            {
                lines.Add(source);
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return new EquatableArray<string>(lines.ToArray());
    }

    private static string? ExperimentalAttribute(AttributeData attribute)
    {
        if (attribute.AttributeClass?.ToDisplayString() != "System.Diagnostics.CodeAnalysis.ExperimentalAttribute" ||
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
