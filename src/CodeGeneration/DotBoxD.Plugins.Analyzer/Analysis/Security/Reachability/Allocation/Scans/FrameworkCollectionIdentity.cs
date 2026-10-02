using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class FrameworkCollectionIdentity
{
    private static readonly byte[] CoreToken = [0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e];
    private static readonly byte[] FrameworkToken = [0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89];
    private static readonly byte[] LibraryToken = [0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a];
    private static readonly byte[] NetStandardToken = [0xcc, 0x7b, 0x13, 0xff, 0xcd, 0x2d, 0xdd, 0x51];

    public static bool IsFrameworkType(INamedTypeSymbol type)
        => type.Locations.Any(static location => location.IsInMetadata) &&
           HasFrameworkToken(type.ContainingAssembly.Identity.PublicKeyToken);

    private static bool HasFrameworkToken(ImmutableArray<byte> token)
        => token.SequenceEqual(CoreToken) || token.SequenceEqual(FrameworkToken) ||
           token.SequenceEqual(LibraryToken) || token.SequenceEqual(NetStandardToken);
}
