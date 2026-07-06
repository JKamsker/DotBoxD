using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class ObsoleteAttributeFormatter
{
    public static string Format(AttributeData attr)
    {
        var message = attr.ConstructorArguments.Length > 0 &&
            attr.ConstructorArguments[0].Value is string value
                ? "\"" + Infrastructure.LiteralHelpers.EscapeStringLiteral(value) + "\""
                : null;

        return message is null
            ? "[global::System.ObsoleteAttribute]"
            : "[global::System.ObsoleteAttribute(" + message + IsErrorArgument(attr) + ")]";
    }

    public static bool IsError(AttributeData attr) =>
        attr.ConstructorArguments.Length > 1 &&
        attr.ConstructorArguments[1].Value is bool constructorIsError &&
        constructorIsError;

    private static string IsErrorArgument(AttributeData attr) =>
        IsError(attr)
            ? ", true"
            : string.Empty;
}
