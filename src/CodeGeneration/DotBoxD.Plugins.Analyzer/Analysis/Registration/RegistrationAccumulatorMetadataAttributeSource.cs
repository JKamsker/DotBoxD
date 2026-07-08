namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;

internal static class RegistrationAccumulatorMetadataAttributeSource
{
    public static EquatableArray<string> TypeAttributes(INamedTypeSymbol type)
        => AttributeLines(type.GetAttributes());

    public static EquatableArray<string> MethodAttributes(IMethodSymbol method)
        => AttributeLines(method.GetAttributes());

    private static EquatableArray<string> AttributeLines(IEnumerable<AttributeData> attributes)
    {
        var lines = new List<string>();
        foreach (var attribute in attributes)
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
            attribute.ConstructorArguments[0].Value is not string diagnosticId)
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
