using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFrameworkAttributeIdentity
{
    public static string? GetName(AttributeData attribute)
    {
        var attributeClass = attribute.AttributeClass;
        return attributeClass is not null &&
               attributeClass.Locations.Any(static location => location.IsInMetadata) &&
               HasFrameworkPublicKeyToken(attributeClass.ContainingAssembly.Identity)
            ? attributeClass.ToDisplayString()
            : null;
    }

    private static bool HasFrameworkPublicKeyToken(AssemblyIdentity identity)
    {
        var token = identity.PublicKeyToken;
        return TokenEquals(token, 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e) ||
               TokenEquals(token, 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89) ||
               TokenEquals(token, 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a);
    }

    private static bool TokenEquals(
        System.Collections.Immutable.ImmutableArray<byte> token,
        byte b0,
        byte b1,
        byte b2,
        byte b3,
        byte b4,
        byte b5,
        byte b6,
        byte b7) =>
        token.Length == 8 &&
        token[0] == b0 && token[1] == b1 && token[2] == b2 && token[3] == b3 &&
        token[4] == b4 && token[5] == b5 && token[6] == b6 && token[7] == b7;
}
