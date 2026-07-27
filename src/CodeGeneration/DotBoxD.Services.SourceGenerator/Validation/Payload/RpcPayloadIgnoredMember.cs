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
               typeName == JsonIgnoreAttribute && IsUnconditionalJsonIgnore(attribute);
    }

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
