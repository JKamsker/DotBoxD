using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class ObsoleteAttributeFormatter
{
    public static (string Source, bool IsError) Format(AttributeData attr)
    {
        var message = attr.ConstructorArguments.Length > 0 &&
            attr.ConstructorArguments[0].Value is string value
                ? "\"" + Infrastructure.LiteralHelpers.EscapeStringLiteral(value) + "\""
                : null;
        var isError = attr.ConstructorArguments.Length > 1 &&
            attr.ConstructorArguments[1].Value is bool constructorIsError &&
            constructorIsError;

        var source = message is null
            ? "[global::System.ObsoleteAttribute]"
            : "[global::System.ObsoleteAttribute(" + message + IsErrorArgument(isError) + ")]";
        return (source, isError);
    }

    private static string IsErrorArgument(bool isError) =>
        isError
            ? ", true"
            : string.Empty;
}
