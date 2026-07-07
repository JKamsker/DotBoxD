using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcPayloadIgnoredMember
{
    private const string IgnoreDataMemberAttribute =
        "System.Runtime.Serialization.IgnoreDataMemberAttribute";
    private const string JsonIgnoreAttribute =
        "System.Text.Json.Serialization.JsonIgnoreAttribute";
    private const string MessagePackIgnoreMemberAttribute =
        "MessagePack.IgnoreMemberAttribute";

    public static bool IsIgnored(ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (IsIgnoreAttribute(attribute.AttributeClass?.ToDisplayString()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIgnoreAttribute(string? typeName) =>
        typeName is IgnoreDataMemberAttribute
            or JsonIgnoreAttribute
            or MessagePackIgnoreMemberAttribute;
}
