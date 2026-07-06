namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;

internal static class RegistrationObsoleteAttributeSource
{
    public static EquatableArray<string> Attributes(ISymbol symbol)
    {
        var lines = new List<string>();
        foreach (var attribute in symbol.GetAttributes())
        {
            if (TryFormat(attribute) is { } source)
            {
                lines.Add(source);
            }
        }

        return EquatableArray<string>.FromOwned(lines.ToArray());
    }

    private static string? TryFormat(AttributeData attribute)
    {
        if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.ObsoleteAttribute", StringComparison.Ordinal))
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
        var needsSeparator = false;
        for (var i = 0; i < attribute.ConstructorArguments.Length; i++)
        {
            if (ConstantSource(attribute.ConstructorArguments[i]) is not { } value)
            {
                return;
            }

            builder.Append(needsSeparator ? ", " : "(").Append(value);
            needsSeparator = true;
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (ConstantSource(argument.Value) is not { } value)
            {
                continue;
            }

            builder.Append(needsSeparator ? ", " : "(")
                .Append(argument.Key)
                .Append(" = ")
                .Append(value);
            needsSeparator = true;
        }

        if (needsSeparator)
        {
            builder.Append(')');
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
