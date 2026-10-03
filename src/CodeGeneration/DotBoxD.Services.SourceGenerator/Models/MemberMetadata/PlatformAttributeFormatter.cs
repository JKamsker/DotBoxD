using System.Linq;
using System.Text;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class PlatformAttributeFormatter
{
    public static bool IsSupported(AttributeData attribute) =>
        attribute.AttributeClass is { } type &&
        type.ToDisplayString() is
            "System.Runtime.Versioning.SupportedOSPlatformAttribute" or
            "System.Runtime.Versioning.UnsupportedOSPlatformAttribute" or
            "System.Runtime.Versioning.ObsoletedOSPlatformAttribute" &&
        ReturnTypeClassifier.IsTrustedFrameworkType(type);

    public static void Append(StringBuilder builder, AttributeData attribute)
    {
        builder.Append("[global::").Append(attribute.AttributeClass!.ToDisplayString()).Append('(');
        var arguments = attribute.ConstructorArguments.Select(FormatArgument).ToList();
        arguments.AddRange(attribute.NamedArguments.Select(argument =>
            argument.Key + " = " + FormatArgument(argument.Value)));
        builder.Append(string.Join(", ", arguments)).AppendLine(")]");
    }

    private static string FormatArgument(TypedConstant argument) =>
        argument.Value is string value
            ? "\"" + LiteralHelpers.EscapeStringLiteral(value) + "\""
            : "null";
}
