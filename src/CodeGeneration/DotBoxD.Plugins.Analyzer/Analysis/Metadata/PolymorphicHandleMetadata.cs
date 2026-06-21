using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed record PolymorphicHandleMetadata(
    INamedTypeSymbol HandleType,
    ITypeSymbol KeyType,
    string KeyManifestTag,
    string KeySandboxTypeSource)
{
    public bool TrySubtype(INamedTypeSymbol subtype, out HandleSubtypeMetadata metadata)
    {
        foreach (var attribute in HandleType.GetAttributes())
        {
            if (!IsAttribute(attribute, DotBoxDGenerationNames.Metadata.HandleSubtypeAttribute) ||
                attribute.ConstructorArguments.Length != 4 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol declaredSubtype ||
                attribute.ConstructorArguments[1].Value is not string discriminator ||
                attribute.ConstructorArguments[2].Value is not string bindingPrefix ||
                attribute.ConstructorArguments[3].Value is not string capability ||
                !SymbolEqualityComparer.Default.Equals(declaredSubtype, subtype))
            {
                continue;
            }

            metadata = new HandleSubtypeMetadata(declaredSubtype, discriminator, bindingPrefix, capability);
            return true;
        }

        metadata = null!;
        return false;
    }

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
                        DotBoxDGenerationNames.Metadata.PolymorphicHandleAttribute) ||
                    attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not string keyMember ||
                    KeyType(current, keyMember) is not { } keyType ||
                    !IsSupportedKey(keyType, out var tag, out var sandboxTypeSource))
                {
                    continue;
                }

                metadata = new PolymorphicHandleMetadata(current, keyType, tag, sandboxTypeSource);
                return true;
            }
        }

        metadata = null!;
        return false;
    }

    private static ITypeSymbol? KeyType(INamedTypeSymbol handleType, string keyMember)
    {
        for (var current = handleType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(keyMember))
            {
                if (member is IPropertySymbol { IsStatic: false } property)
                {
                    return property.Type;
                }

                if (member is IFieldSymbol { IsStatic: false } field)
                {
                    return field.Type;
                }
            }
        }

        return null;
    }

    private static bool IsSupportedKey(ITypeSymbol keyType, out string tag, out string sandboxTypeSource)
    {
        tag = SandboxTypeSourceEmitter.ManifestTag(keyType);
        sandboxTypeSource = SandboxTypeSourceEmitter.TryEmit(keyType) ?? string.Empty;
        return tag is DotBoxDGenerationNames.ManifestTypes.Int
            or DotBoxDGenerationNames.ManifestTypes.Long
            or DotBoxDGenerationNames.ManifestTypes.Guid
            or DotBoxDGenerationNames.ManifestTypes.String;
    }
}
