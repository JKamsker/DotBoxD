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
        var attributeType = attribute.AttributeClass;
        if (attributeType is null || !attributeType.Locations.Any(static location => location.IsInMetadata))
        {
            return false;
        }

        var identity = attributeType.ContainingAssembly.Identity;
        return attributeType.ToDisplayString() switch
        {
            IgnoreDataMemberAttribute => IsFrameworkToken(identity.PublicKeyToken),
            MessagePackIgnoreMemberAttribute => identity.Name is "MessagePack" or "MessagePack.Annotations" &&
                IsMessagePackToken(identity.PublicKeyToken),
            JsonIgnoreAttribute => IsSystemTextJsonAttribute(attributeType) &&
                IsUnconditionalJsonIgnore(attribute),
            _ => false
        };
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

    private static bool IsMessagePackToken(System.Collections.Immutable.ImmutableArray<byte> token) =>
        token.Length == 8 &&
        token[0] == 0xb4 && token[1] == 0xa0 && token[2] == 0x36 && token[3] == 0x95 &&
        token[4] == 0x45 && token[5] == 0xf0 && token[6] == 0xa1 && token[7] == 0xbe;

    private static bool IsFrameworkToken(System.Collections.Immutable.ImmutableArray<byte> token) =>
        IsMicrosoftToken(token) || IsEcmaToken(token);

    private static bool IsMicrosoftToken(System.Collections.Immutable.ImmutableArray<byte> token) =>
        token.Length == 8 &&
        token[0] == 0xb0 && token[1] == 0x3f && token[2] == 0x5f && token[3] == 0x7f &&
        token[4] == 0x11 && token[5] == 0xd5 && token[6] == 0x0a && token[7] == 0x3a;

    private static bool IsEcmaToken(System.Collections.Immutable.ImmutableArray<byte> token) =>
        token.Length == 8 &&
        token[0] == 0xb7 && token[1] == 0x7a && token[2] == 0x5c && token[3] == 0x56 &&
        token[4] == 0x19 && token[5] == 0x34 && token[6] == 0xe0 && token[7] == 0x89;

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
