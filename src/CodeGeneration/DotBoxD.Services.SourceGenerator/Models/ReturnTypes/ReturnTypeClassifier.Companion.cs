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

            if (candidate.DeclaredAccessibility != Accessibility.Public ||
                !ImplementsService(candidate, serviceType, ct))
            {
                continue;
            }

            foreach (var constructor in candidate.InstanceConstructors)
            {
                ct.ThrowIfCancellationRequested();

                if (constructor is { DeclaredAccessibility: Accessibility.Public, Parameters.Length: 2 } &&
                    SubServiceReturnTypeReader.IsRpcInvokerType(constructor.Parameters[0].Type, rpcInvokerType) &&
                    constructor.Parameters[1].Type.SpecialType == SpecialType.System_String)
                {
                    return true;
                }
            }
        }

        return false;
    }

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
