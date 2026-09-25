namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;

internal static class RegistrationPlatformAttributeSource
{
    public static EquatableArray<string> Attributes(ISymbol symbol, Compilation compilation)
    {
        var platformAttribute = compilation.GetTypeByMetadataName(
            "System.Runtime.Versioning.OSPlatformAttribute");
        var lines = new List<string>();

        foreach (var attribute in symbol.GetAttributes())
        {
            if (TryFormat(attribute, platformAttribute) is { } source)
            {
                lines.Add(source);
            }
        }

        return EquatableArray<string>.FromOwned(lines.ToArray());
    }

    private static string? TryFormat(AttributeData attribute, INamedTypeSymbol? platformAttribute)
    {
        if (!DerivesFrom(attribute.AttributeClass, platformAttribute) ||
            attribute.AttributeClass is not { } attributeClass ||
            !TryFormatArguments(attribute, out var arguments))
        {
            return null;
        }

        return "[" + attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "(" + string.Join(", ", arguments) + ")]";
    }

    private static bool DerivesFrom(INamedTypeSymbol? type, INamedTypeSymbol? platformAttribute)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, platformAttribute))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFormatArguments(AttributeData attribute, out List<string> arguments)
    {
        arguments = new List<string>(attribute.ConstructorArguments.Length + attribute.NamedArguments.Length);
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (StringLiteral(argument) is not { } value)
            {
                return false;
            }

            arguments.Add(value);
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (StringLiteral(argument.Value) is not { } value)
            {
                return false;
            }

            arguments.Add(argument.Key + " = " + value);
        }

        return true;
    }

    private static string? StringLiteral(TypedConstant constant)
        => constant.Value switch
        {
            null when constant.Type?.SpecialType == SpecialType.System_String => "null",
            string value => LiteralReader.StringLiteral(value),
            _ => null,
        };
}
