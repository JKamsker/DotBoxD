using System.Linq;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcPayloadIgnoredMember
{
    private const string JsonIgnoreConditionAlways = "Always";
    private const string JsonIgnoreConditionProperty = "Condition";
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
            if (IsIgnoreAttribute(attribute))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIgnoreAttribute(AttributeData attribute)
    {
        var typeName = attribute.AttributeClass?.ToDisplayString();
        return typeName is IgnoreDataMemberAttribute or MessagePackIgnoreMemberAttribute ||
               typeName == JsonIgnoreAttribute &&
               IsSystemTextJsonAttribute(attribute.AttributeClass) &&
               IsUnconditionalJsonIgnore(attribute);
    }

    private static bool IsSystemTextJsonAttribute(INamedTypeSymbol? attributeType)
    {
        var token = attributeType?.ContainingAssembly.Identity.PublicKeyToken ?? default;
        return attributeType is not null &&
               attributeType.Locations.Any(static location => location.IsInMetadata) &&
               attributeType.ContainingAssembly.Name == "System.Text.Json" &&
               IsSystemTextJsonToken(token);
    }

    private static bool IsSystemTextJsonToken(System.Collections.Immutable.ImmutableArray<byte> token) =>
        token.Length == 8 &&
        token[0] == 0xcc && token[1] == 0x7b && token[2] == 0x13 && token[3] == 0xff &&
        token[4] == 0xcd && token[5] == 0x2d && token[6] == 0xdd && token[7] == 0x51;

    private static bool IsUnconditionalJsonIgnore(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == JsonIgnoreConditionProperty)
            {
                return IsAlwaysJsonIgnoreCondition(argument.Value);
            }
        }

        return true;
    }

    private static bool IsAlwaysJsonIgnoreCondition(TypedConstant condition)
    {
        if (condition.Type is null)
        {
            return false;
        }

        foreach (var member in condition.Type.GetMembers(JsonIgnoreConditionAlways))
        {
            if (member is IFieldSymbol { ConstantValue: { } value })
            {
                return Equals(condition.Value, value);
            }
        }

        return false;
    }
}
