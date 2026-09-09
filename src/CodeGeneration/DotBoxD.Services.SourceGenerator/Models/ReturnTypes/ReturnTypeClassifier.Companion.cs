using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class ReturnTypeClassifier
{
    internal static bool HasGeneratedProxyCompanion(INamedTypeSymbol serviceType, CancellationToken ct)
    {
        var proxyName = NamingHelpers.StripInterfacePrefix(serviceType.Name) + "Proxy";
        foreach (var candidate in serviceType.ContainingNamespace.GetTypeMembers(proxyName))
        {
            ct.ThrowIfCancellationRequested();

            if (candidate.DeclaredAccessibility != Accessibility.Public ||
                !ImplementsService(candidate, serviceType, ct))
            {
                continue;
            }

            foreach (var constructor in candidate.InstanceConstructors)
            {
                ct.ThrowIfCancellationRequested();

                if (constructor is { DeclaredAccessibility: Accessibility.Public, Parameters.Length: 2 } &&
                    SubServiceReturnTypeReader.IsRpcInvokerType(constructor.Parameters[0].Type) &&
                    constructor.Parameters[1].Type.SpecialType == SpecialType.System_String &&
                    CanConstructProxy(candidate, constructor, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CanConstructProxy(
        INamedTypeSymbol candidate,
        IMethodSymbol constructor,
        CancellationToken ct)
        => !HasRequiredMembers(candidate, ct) ||
           HasSetsRequiredMembersAttribute(constructor);

    private static bool HasRequiredMembers(INamedTypeSymbol candidate, CancellationToken ct)
    {
        for (var current = candidate; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                ct.ThrowIfCancellationRequested();

                if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSetsRequiredMembersAttribute(IMethodSymbol constructor)
    {
        foreach (var attribute in constructor.GetAttributes())
        {
            var type = attribute.AttributeClass;
            if (type?.ToDisplayString() != "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute" ||
                !IsMetadataType(type))
            {
                continue;
            }

            var token = type.ContainingAssembly.Identity.PublicKeyToken;
            if (token.Length == 8 &&
                TokenEquals(token, 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMetadataType(INamedTypeSymbol type)
    {
        foreach (var location in type.Locations)
        {
            if (location.IsInMetadata)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsService(
        INamedTypeSymbol candidate,
        INamedTypeSymbol serviceType,
        CancellationToken ct)
    {
        foreach (var implemented in candidate.AllInterfaces)
        {
            ct.ThrowIfCancellationRequested();

            if (SymbolEqualityComparer.Default.Equals(implemented, serviceType))
            {
                return true;
            }
        }

        return false;
    }
}
