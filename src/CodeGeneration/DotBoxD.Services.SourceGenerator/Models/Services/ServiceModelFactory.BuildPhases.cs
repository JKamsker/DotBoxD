using System;
using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class ServiceModelFactory
{
    public static ServiceResult? GetServiceResult(GeneratorSyntaxContext context, CancellationToken ct)
    {
        try
        {
            return BuildServiceResult(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var name = context.Node is InterfaceDeclarationSyntax declaration
                ? declaration.Identifier.ValueText
                : "<unknown>";
            return new ServiceResult(
                Model: null,
                Error: new GeneratorError(name, ex.ToString()),
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
                PropertyLocations: EquatableArray<DiagnosticLocation>.Empty,
                ServiceLocation: default,
                QualifiedInterfaceName: string.Empty,
                ServiceDiagnostic: null);
        }
    }

    private static ServiceResult? ValidateInterfaceSymbol(
        INamedTypeSymbol interfaceSymbol,
        ServiceBuildContext context)
    {
        if (interfaceSymbol.IsGenericType)
        {
            return RejectedService(
                context.DisplayName,
                "generic service interfaces are not supported; declare a non-generic interface and forward to a generic helper if needed",
                context.ServiceLocation,
                context.QualifiedInterfaceName);
        }

        if (interfaceSymbol.ContainingType is not null)
        {
            return RejectedService(
                context.DisplayName,
                "nested service interfaces are not supported; declare the interface at namespace scope",
                context.ServiceLocation,
                context.QualifiedInterfaceName);
        }

        return interfaceSymbol.DeclaredAccessibility != Accessibility.Public
            ? RejectedService(
                context.DisplayName,
                "service interfaces must be public because generated proxy, dispatcher, and extension APIs are public",
                context.ServiceLocation,
                context.QualifiedInterfaceName)
            : null;
    }

    private static bool TryCollectServiceMembers(
        INamedTypeSymbol interfaceSymbol,
        Compilation compilation,
        ServiceBuildContext context,
        CancellationToken ct,
        out ServiceMembers members,
        out ServiceResult rejected)
    {
        var interfaceMethods = new List<IMethodSymbol>();
        var interfaceProperties = new List<IPropertySymbol>();
        var unsupportedMemberDiagnostic = ServiceShapeValidator.CollectMembers(
            interfaceSymbol,
            compilation,
            interfaceMethods,
            interfaceProperties,
            ct);
        if (unsupportedMemberDiagnostic is not null)
        {
            members = default!;
            rejected = RejectedService(
                context.DisplayName,
                unsupportedMemberDiagnostic.Value.Reason,
                unsupportedMemberDiagnostic.Value.Location,
                context.QualifiedInterfaceName);
            return false;
        }

        return TryCollectUniqueProperties(
            interfaceSymbol,
            context,
            interfaceMethods,
            interfaceProperties,
            ct,
            out members,
            out rejected);
    }

    private static bool TryCollectUniqueProperties(
        INamedTypeSymbol interfaceSymbol,
        ServiceBuildContext context,
        List<IMethodSymbol> interfaceMethods,
        List<IPropertySymbol> interfaceProperties,
        CancellationToken ct,
        out ServiceMembers members,
        out ServiceResult rejected)
    {
        var effectiveProperties = new List<IPropertySymbol>();
        var duplicatePropertyDiagnostic = InheritedPropertyDeduplicator.CollectUnique(
            interfaceProperties,
            interfaceMethods,
            NamingHelpers.StripInterfacePrefix(interfaceSymbol.Name) + "Proxy",
            effectiveProperties,
            ct);
        if (duplicatePropertyDiagnostic is null)
        {
            members = new ServiceMembers(interfaceMethods, effectiveProperties);
            rejected = default;
            return true;
        }

        members = default!;
        rejected = RejectedService(
            context.DisplayName,
            duplicatePropertyDiagnostic.Value.Reason,
            duplicatePropertyDiagnostic.Value.Location,
            context.QualifiedInterfaceName);
        return false;
    }

    private static bool TryApplyInheritedMethod(
        ServiceBuildContext context,
        IMethodSymbol methodSymbol,
        string signatureKey,
        Dictionary<string, IMethodSymbol> seenSignatures,
        Dictionary<string, int> seenSignatureIndexes,
        List<MethodModel> methods,
        CancellationToken ct,
        out ServiceResult rejected,
        out bool hasRejected)
    {
        rejected = default;
        hasRejected = false;
        if (!seenSignatures.TryGetValue(signatureKey, out var existingMethod))
        {
            return false;
        }

        if (TryRejectIncompatibleInheritedMethod(context, existingMethod, methodSymbol, ct, out rejected))
        {
            hasRejected = true;
            return true;
        }

        var existingIndex = seenSignatureIndexes[signatureKey];
        methods[existingIndex] = InheritedMethodDeduplicator.AddAdditionalExplicitImplementation(
            methods[existingIndex],
            methodSymbol.ContainingType);
        return true;
    }

    private static bool TryRejectIncompatibleInheritedMethod(
        ServiceBuildContext context,
        IMethodSymbol existingMethod,
        IMethodSymbol methodSymbol,
        CancellationToken ct,
        out ServiceResult rejected)
    {
        var reason = InheritedMethodDeduplicator.GetDuplicateSignatureRejectionReason(existingMethod, methodSymbol, ct);
        if (reason is null)
        {
            rejected = default;
            return false;
        }

        rejected = RejectedService(
            context.DisplayName,
            reason,
            DiagnosticLocationFactory.FromSymbol(methodSymbol),
            context.QualifiedInterfaceName);
        return true;
    }

    private sealed record ServiceBuildContext(
        string DisplayName,
        DiagnosticLocation ServiceLocation,
        string ServiceNamespace,
        string QualifiedInterfaceName);

    private sealed record ServiceMembers(
        List<IMethodSymbol> Methods,
        List<IPropertySymbol> Properties);
}
