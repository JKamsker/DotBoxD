using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcTypeMetadataAttributeSource
{
    private const string ExperimentalAttributeMetadataName = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";
    private const string ObsoleteAttributeMetadataName = "System.ObsoleteAttribute";

    public static void Append(
        StringBuilder builder,
        INamedTypeSymbol sourceType,
        string indent,
        Compilation compilation)
    {
        var experimentalAttribute = compilation.GetTypeByMetadataName(ExperimentalAttributeMetadataName);
        var obsoleteAttribute = compilation.GetTypeByMetadataName(ObsoleteAttributeMetadataName);
        foreach (var attribute in sourceType.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, experimentalAttribute))
            {
                if (HasStringDiagnosticId(attribute))
                {
                    AppendAttribute(
                        builder,
                        attribute,
                        indent,
                        "global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute");
                }
            }
            else if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, obsoleteAttribute))
            {
                AppendAttribute(builder, attribute, indent, "global::System.ObsoleteAttribute");
            }
        }
    }

    public static IEnumerable<string> ExperimentalDiagnosticIds(INamedTypeSymbol sourceType, Compilation compilation)
    {
        var experimentalAttribute = compilation.GetTypeByMetadataName(ExperimentalAttributeMetadataName);
        foreach (var attribute in sourceType.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, experimentalAttribute) &&
                TryGetPragmaSafeExperimentalDiagnosticId(attribute, out var diagnosticId))
            {
                yield return diagnosticId;
            }
        }
    }

    private static bool HasStringDiagnosticId(AttributeData attribute) =>
        attribute.ConstructorArguments.Length == 1 &&
        attribute.ConstructorArguments[0].Value is string;

    private static bool TryGetPragmaSafeExperimentalDiagnosticId(AttributeData attribute, out string diagnosticId)
    {
        if (attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0].Value is string value &&
            IsPragmaWarningIdentifier(value))
        {
            diagnosticId = value;
            return true;
        }

        diagnosticId = "";
        return false;
    }

    private static void AppendAttribute(
        StringBuilder builder,
        AttributeData attribute,
        string indent,
        string attributeType)
    {
        var source = new StringBuilder();
        source.Append(indent).Append('[').Append(attributeType);
        if (attribute.ConstructorArguments.Length == 0 &&
            !HasSupportedNamedArguments(attribute))
        {
            source.AppendLine("]");
            builder.Append(source);
            return;
        }

        source.Append('(');
        var needsSeparator = false;
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (!TryAppendArgument(source, argument, ref needsSeparator))
            {
                return;
            }
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (!TryAppendNamedArgument(source, argument, ref needsSeparator))
            {
                return;
            }
        }

        source.AppendLine(")]");
        builder.Append(source);
    }

    private static bool HasSupportedNamedArguments(AttributeData attribute)
        => attribute.NamedArguments.Any(static argument => argument.Key is "DiagnosticId" or "UrlFormat" or "Message");

    private static bool TryAppendNamedArgument(
        StringBuilder builder,
        KeyValuePair<string, TypedConstant> argument,
        ref bool needsSeparator)
    {
        if (argument.Key is not ("DiagnosticId" or "UrlFormat" or "Message"))
        {
            return true;
        }

        AppendSeparator(builder, ref needsSeparator);
        builder.Append(argument.Key).Append(" = ");
        return TryAppendArgumentValue(builder, argument.Value);
    }

    private static bool TryAppendArgument(StringBuilder builder, TypedConstant argument, ref bool needsSeparator)
    {
        AppendSeparator(builder, ref needsSeparator);
        return TryAppendArgumentValue(builder, argument);
    }

    private static bool TryAppendArgumentValue(StringBuilder builder, TypedConstant argument)
    {
        if (argument.Value is null)
        {
            builder.Append("null");
            return true;
        }

        switch (argument.Value)
        {
            case string value:
                builder.Append(LiteralReader.StringLiteral(value));
                return true;

            case bool value:
                builder.Append(value ? "true" : "false");
                return true;

            default:
                return false;
        }
    }

    private static void AppendSeparator(StringBuilder builder, ref bool needsSeparator)
    {
        if (needsSeparator)
        {
            builder.Append(", ");
        }
        else
        {
            needsSeparator = true;
        }
    }

    private static bool IsPragmaWarningIdentifier(string value)
        => value.Length > 0 &&
           value.All(static ch => IsAsciiLetterOrDigit(ch) || ch == '_');

    private static bool IsAsciiLetterOrDigit(char value)
        => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
