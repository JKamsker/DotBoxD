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
                HasErrorObsoleteAttribute(candidate, ct) ||
                !ImplementsService(candidate, serviceType, ct))
            {
                continue;
            }

            foreach (var constructor in candidate.InstanceConstructors)
            {
                ct.ThrowIfCancellationRequested();

                if (constructor is { DeclaredAccessibility: Accessibility.Public, Parameters.Length: 2 } &&
                    !HasErrorObsoleteAttribute(constructor, ct) &&
                    SubServiceReturnTypeReader.IsRpcInvokerType(constructor.Parameters[0].Type) &&
                    constructor.Parameters[1].Type.SpecialType == SpecialType.System_String)
                {
                    return true;
                }
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
