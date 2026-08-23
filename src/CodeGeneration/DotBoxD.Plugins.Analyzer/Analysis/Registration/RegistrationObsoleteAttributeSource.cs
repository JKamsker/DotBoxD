namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;

internal static class RegistrationObsoleteAttributeSource
{
    public static EquatableArray<string> Attributes(ISymbol symbol, Compilation compilation)
    {
        var lines = new List<string>();
        var obsoleteAttribute = compilation.GetTypeByMetadataName("System.ObsoleteAttribute");
        foreach (var attribute in symbol.GetAttributes())
        {
            if (TryFormat(attribute, obsoleteAttribute) is { } source)
            {
                lines.Add(source);
            }
        }

        return EquatableArray<string>.FromOwned(lines.ToArray());
    }

    private static string? TryFormat(AttributeData attribute, INamedTypeSymbol? obsoleteAttribute)
    {
        if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, obsoleteAttribute))
        {
            return null;
        }

        var builder = new StringBuilder("[global::System.ObsoleteAttribute");
        AppendArguments(builder, attribute);
        builder.Append(']');
        return builder.ToString();
    }

    private static void AppendArguments(StringBuilder builder, AttributeData attribute)
    {
        var arguments = new List<string>(attribute.ConstructorArguments.Length + attribute.NamedArguments.Length);
        for (var i = 0; i < attribute.ConstructorArguments.Length; i++)
        {
            if (ConstantSource(attribute.ConstructorArguments[i]) is not { } value)
            {
                return;
            }

            arguments.Add(value);
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (ConstantSource(argument.Value) is not { } value)
            {
                continue;
            }

            arguments.Add(argument.Key + " = " + value);
        }

        if (arguments.Count > 0)
        {
            builder.Append('(')
                .Append(string.Join(", ", arguments))
                .Append(')');
        }
    }

    private static string? ConstantSource(TypedConstant constant)
        => constant.Value switch
        {
            null when constant.Type?.SpecialType == SpecialType.System_String => "null",
            string value => LiteralReader.StringLiteral(value),
            bool value => value ? "true" : "false",
            _ => null,
        };
}
