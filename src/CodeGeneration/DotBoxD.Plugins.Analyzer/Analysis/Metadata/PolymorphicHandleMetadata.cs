using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed record PolymorphicHandleMetadata(
    INamedTypeSymbol HandleType,
    string KeyMember,
    ISymbol KeyMemberSymbol,
    ITypeSymbol KeyType,
    string KeyManifestTag,
    string KeySandboxTypeSource)
{
    public bool TrySubtype(INamedTypeSymbol subtype, out HandleSubtypeMetadata metadata)
    {
        foreach (var attribute in HandleType.GetAttributes())
        {
            if (!TryReadSubtype(attribute, out var declaredSubtype, out var discriminator, out var bindingPrefix, out var capability))
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(declaredSubtype, subtype))
            {
                continue;
            }

            metadata = new HandleSubtypeMetadata(declaredSubtype, discriminator, bindingPrefix, capability);
            return true;
        }

        metadata = null!;
        return false;
    }

    private static bool TryReadSubtype(
        AttributeData attribute,
        out INamedTypeSymbol declaredSubtype,
        out string discriminator,
        out string bindingPrefix,
        out string capability)
    {
        declaredSubtype = null!;
        discriminator = string.Empty;
        bindingPrefix = string.Empty;
        capability = string.Empty;
        if (!IsAttribute(attribute, DotBoxDMetadataNames.HandleSubtypeAttribute))
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length != 4)
        {
            return false;
        }

        if (attribute.ConstructorArguments[0].Value is not INamedTypeSymbol subtype)
        {
            return false;
        }

        if (!TryReadSubtypeMetadata(attribute, out discriminator, out bindingPrefix, out capability))
        {
            return false;
        }

        declaredSubtype = subtype;
        return true;
    }

    private static bool TryReadSubtypeMetadata(
        AttributeData attribute,
        out string discriminator,
        out string bindingPrefix,
        out string capability)
    {
        discriminator = string.Empty;
        bindingPrefix = string.Empty;
        capability = string.Empty;
        if (attribute.ConstructorArguments[1].Value is not string discriminatorValue)
        {
            return false;
        }

        if (attribute.ConstructorArguments[2].Value is not string bindingPrefixValue)
        {
            return false;
        }

        if (attribute.ConstructorArguments[3].Value is not string capabilityValue)
        {
            return false;
        }

        if (!IsValidSubtypeMetadata(discriminatorValue, bindingPrefixValue, capabilityValue))
        {
            return false;
        }

        discriminator = discriminatorValue;
        bindingPrefix = bindingPrefixValue;
        capability = capabilityValue;
        return true;
    }

    private static bool IsValidSubtypeMetadata(string discriminator, string bindingPrefix, string capability)
        => HasIdentifierGrammar(discriminator) &&
           HasDottedIdentifierGrammar(bindingPrefix) &&
           HasDottedIdentifierGrammar(capability);

    private static bool HasDottedIdentifierGrammar(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var expectingSegmentStart = true;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '.')
            {
                if (expectingSegmentStart)
                {
                    return false;
                }

                expectingSegmentStart = true;
                continue;
            }

            if (expectingSegmentStart)
            {
                if (!IsIdentifierStart(ch))
                {
                    return false;
                }

                expectingSegmentStart = false;
                continue;
            }

            if (!IsIdentifierPart(ch))
            {
                return false;
            }
        }

        return !expectingSegmentStart;
    }

    private static bool HasIdentifierGrammar(string value)
    {
        if (value.Length == 0 || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char ch)
        => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    private static bool IsIdentifierPart(char ch)
        => IsIdentifierStart(ch) || ch is >= '0' and <= '9';

    internal static bool IsAttribute(AttributeData attribute, string metadataName)
        => string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal);
}

internal sealed record HandleSubtypeMetadata(
    INamedTypeSymbol Subtype,
    string Discriminator,
    string BindingPrefix,
    string Capability)
{
    public string DiscriminatorBindingId => BindingPrefix + ".is";
}

internal static class PolymorphicHandleMetadataReader
{
    public static bool TryResolve(ITypeSymbol type, out PolymorphicHandleMetadata metadata)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (!PolymorphicHandleMetadata.IsAttribute(
                        attribute,
                        DotBoxDMetadataNames.PolymorphicHandleAttribute))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not string keyMember ||
                    string.IsNullOrWhiteSpace(keyMember))
                {
                    throw InvalidHandleMetadata(current);
                }

                if (KeyMemberSymbol(current, keyMember) is not { } keyMemberSymbol ||
                    !IsSupportedKey(KeyType(keyMemberSymbol), out var tag, out var sandboxTypeSource))
                {
                    throw InvalidHandleMetadata(current);
                }

                metadata = new PolymorphicHandleMetadata(
                    current,
                    keyMember,
                    keyMemberSymbol,
                    KeyType(keyMemberSymbol),
                    tag,
                    sandboxTypeSource);
                return true;
            }
        }

        metadata = null!;
        return false;
    }

    private static ISymbol? KeyMemberSymbol(INamedTypeSymbol handleType, string keyMember)
    {
        for (var current = handleType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(keyMember))
            {
                if (member is IPropertySymbol
                    {
                        DeclaredAccessibility: Accessibility.Public,
                        IsStatic: false,
                        Parameters.Length: 0,
                        GetMethod.DeclaredAccessibility: Accessibility.Public
                    } property)
                {
                    return property;
                }

                if (member is IFieldSymbol
                    {
                        DeclaredAccessibility: Accessibility.Public,
                        IsStatic: false
                    } field)
                {
                    return field;
                }
            }
        }

        return null;
    }

    private static ITypeSymbol KeyType(ISymbol keyMember)
        => keyMember switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => throw new NotSupportedException()
        };

    private static bool IsSupportedKey(ITypeSymbol keyType, out string tag, out string sandboxTypeSource)
    {
        tag = SandboxTypeSourceEmitter.ManifestTag(keyType);
        sandboxTypeSource = SandboxTypeSourceEmitter.TryEmit(keyType) ?? string.Empty;
        return keyType.SpecialType is SpecialType.System_Int32
                or SpecialType.System_Int64
                or SpecialType.System_String ||
            DotBoxDRpcTypeMapper.IsGuid(keyType);
    }

    private static NotSupportedException InvalidHandleMetadata(INamedTypeSymbol handleType)
        => new($"Polymorphic handle '{handleType.ToDisplayString()}' must declare a public readable non-indexer key member of type int, long, Guid, or string.");
}
