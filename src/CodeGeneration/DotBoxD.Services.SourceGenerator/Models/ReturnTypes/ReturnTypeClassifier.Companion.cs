using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class ReturnTypeClassifier
{
    private const string RpcInvokerMetadata = "DotBoxD.Services.Server.IRpcInvoker";

    internal static bool HasGeneratedProxyCompanion(INamedTypeSymbol serviceType, CancellationToken ct)
    {
        var rpcInvokerType = GetRpcInvokerType(serviceType, ct);
        if (rpcInvokerType is null)
        {
            return false;
        }

        var proxyName = NamingHelpers.StripInterfacePrefix(serviceType.Name) + "Proxy";
        foreach (var candidate in serviceType.ContainingNamespace.GetTypeMembers(proxyName))
        {
            ct.ThrowIfCancellationRequested();

            if (!IsUsableProxyCandidate(candidate, serviceType, ct))
            {
                continue;
            }

            if (HasUsableProxyConstructor(candidate, rpcInvokerType, ct))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUsableProxyCandidate(
        INamedTypeSymbol candidate,
        INamedTypeSymbol serviceType,
        CancellationToken ct)
        => !candidate.HasUnsupportedMetadata &&
           candidate.DeclaredAccessibility == Accessibility.Public &&
           !candidate.IsAbstract &&
           !candidate.IsGenericType &&
           !HasErrorObsoleteAttribute(candidate, ct) &&
           !IsExperimental(candidate, ct) &&
           !RequiresPreviewFeatures(candidate, ct) &&
           ImplementsService(candidate, serviceType, ct);

    private static bool HasUsableProxyConstructor(
        INamedTypeSymbol candidate,
        INamedTypeSymbol rpcInvokerType,
        CancellationToken ct)
    {
        foreach (var constructor in candidate.InstanceConstructors)
        {
            ct.ThrowIfCancellationRequested();

            if (IsUsableProxyConstructor(candidate, constructor, rpcInvokerType, ct))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUsableProxyConstructor(
        INamedTypeSymbol candidate,
        IMethodSymbol constructor,
        INamedTypeSymbol rpcInvokerType,
        CancellationToken ct)
        => constructor is { DeclaredAccessibility: Accessibility.Public, Parameters.Length: 2 } &&
           !HasErrorObsoleteAttribute(constructor, ct) &&
           !IsExperimental(constructor, ct) &&
           !RequiresPreviewFeatures(constructor, ct) &&
           constructor.Parameters[0] is { RefKind: RefKind.None } invoker &&
           constructor.Parameters[1] is { RefKind: RefKind.None } instanceId &&
           SubServiceReturnTypeReader.IsRpcInvokerType(invoker.Type, rpcInvokerType) &&
           instanceId.Type.SpecialType == SpecialType.System_String &&
           CanConstructProxy(candidate, constructor, ct);

    private static INamedTypeSymbol? GetRpcInvokerType(INamedTypeSymbol serviceType, CancellationToken ct)
    {
        foreach (var attribute in serviceType.GetAttributes())
        {
            for (var type = attribute.AttributeClass; type is not null; type = type.BaseType)
            {
                ct.ThrowIfCancellationRequested();

                if (type.ToDisplayString() == ServicesGeneratorTypeNames.RpcServiceAttribute &&
                    ServicesGeneratorTypeNames.IsRpcServiceAttribute(type))
                {
                    return type.ContainingAssembly.GetTypeByMetadataName(RpcInvokerMetadata);
                }
            }
        }

        return null;
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

    private static bool HasErrorObsoleteAttribute(ISymbol symbol, CancellationToken ct)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if (attributeClass?.ToDisplayString() != "System.ObsoleteAttribute" ||
                !IsFrameworkAssembly(attributeClass.ContainingAssembly.Name))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 1 &&
                attribute.ConstructorArguments[1].Value is bool isError &&
                isError)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFrameworkAssembly(string assemblyName) =>
        assemblyName is "mscorlib" or "netstandard" or "System.Private.CoreLib" or "System.Runtime";

    private static bool IsExperimental(ISymbol symbol, CancellationToken ct)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attribute.AttributeClass is { } attributeType &&
                attributeType.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExperimentalAttribute" &&
                IsTrustedFrameworkType(attributeType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresPreviewFeatures(ISymbol symbol, CancellationToken ct)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attribute.AttributeClass is { } attributeType &&
                attributeType.ToDisplayString() == "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute" &&
                IsTrustedFrameworkType(attributeType))
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
