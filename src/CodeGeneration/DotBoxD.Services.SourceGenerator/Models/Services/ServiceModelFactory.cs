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
    private const string CancellationTokenFullName = ServicesGeneratorTypeNames.CancellationTokenMetadata;

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

    private static ServiceResult? BuildServiceResult(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (!ServiceCandidateSelector.TryGet(context, ct, out var interfaceSymbol, out var serviceAttribute))
        {
            return null;
        }

        var displayName = interfaceSymbol.ToDisplayString();
        var serviceLocation = DiagnosticLocationFactory.FromSymbol(interfaceSymbol);
        var serviceNamespace = GetNamespace(interfaceSymbol.ContainingNamespace);
        var qualifiedInterfaceName = IdentifierHelpers.QualifyTypeName(
            serviceNamespace,
            interfaceSymbol.Name);

        var buildContext = new ServiceBuildContext(displayName, serviceLocation, serviceNamespace, qualifiedInterfaceName);
        if (ValidateServiceSymbol(interfaceSymbol, buildContext, ct, out var obsoleteAttribute) is { } rejectedService)
        {
            return rejectedService;
        }

        if (!TryCollectServiceMembers(interfaceSymbol, buildContext, ct, out var members, out var rejectedMembers))
        {
            return rejectedMembers;
        }

        ct.ThrowIfCancellationRequested();

        var serviceName = GetConfiguredServiceName(serviceAttribute) ?? interfaceSymbol.Name;
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            // An explicit empty/whitespace [RpcService(Name = "")] compiles but no inbound call can ever
            // match the empty wire name, so every dispatch fails at runtime. Reject it at build time.
            return RejectedService(
                buildContext.DisplayName,
                "[RpcService(Name = ...)] wire name must not be empty or whitespace",
                buildContext.ServiceLocation,
                buildContext.QualifiedInterfaceName);
        }

        var cancellationTokenSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(CancellationTokenFullName);
        var methods = new List<MethodModel>();
        var properties = new List<ServicePropertyModel>();
        var methodLocations = new List<DiagnosticLocation>();
        var propertyLocations = new List<DiagnosticLocation>();
        var methodDiagnostics = new List<MethodDiagnostic>();
        var seenSignatures = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
        var seenSignatureIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var validationCache = SharedRpcTypeValidationCache.Get(context.SemanticModel.Compilation);

        foreach (var methodSymbol in members.Methods)
        {
            ct.ThrowIfCancellationRequested();

            var sigKey = MethodSignatureFacts.GetSignatureKey(methodSymbol, ct);
            if (TryApplyInheritedMethod(
                    buildContext,
                    methodSymbol,
                    sigKey,
                    seenSignatures,
                    seenSignatureIndexes,
                    methods,
                    ct,
                    out var rejectedMethod,
                    out var hasRejectedMethod))
            {
                if (hasRejectedMethod)
                {
                    return rejectedMethod;
                }

                continue;
            }

            seenSignatures[sigKey] = methodSymbol;
            seenSignatureIndexes[sigKey] = methods.Count;

            var method = MethodModelFactory.Build(
                buildContext.DisplayName,
                methodSymbol,
                cancellationTokenSymbol,
                validationCache,
                methodDiagnostics,
                ct,
                out var methodLocation);

            methods.Add(method);
            methodLocations.Add(methodLocation);
        }

        foreach (var propertySymbol in members.Properties)
        {
            if (!ServicePropertyModelFactory.TryBuild(propertySymbol, ct, out var property, out var propertyLocation))
            {
                continue;
            }

            properties.Add(property);
            propertyLocations.Add(propertyLocation);
        }

        WireNameValidator.MarkDuplicateWireNames(displayName, methods, methodLocations, methodDiagnostics, ct);
        var experimentalAttribute = ExperimentalAttributeFormatter.From(interfaceSymbol);

        return new ServiceResult(
            Model: new ServiceModel(
                Namespace: buildContext.ServiceNamespace,
                InterfaceName: interfaceSymbol.Name,
                ServiceName: LiteralHelpers.EscapeStringLiteral(serviceName),
                Methods: methods.ToEquatableArray(),
                Properties: properties.ToEquatableArray(),
                RawServiceName: serviceName,
                ObsoleteAttribute: obsoleteAttribute.Source,
                TypeAttributePrefix: experimentalAttribute.AttributePrefix,
                ExperimentalDiagnosticId: experimentalAttribute.DiagnosticId),
            Error: null,
            MethodDiagnostics: methodDiagnostics.ToEquatableArray(),
            MethodLocations: methodLocations.ToEquatableArray(),
            PropertyLocations: propertyLocations.ToEquatableArray(),
            ServiceLocation: buildContext.ServiceLocation,
            QualifiedInterfaceName: buildContext.QualifiedInterfaceName,
            ServiceDiagnostic: null);
    }

    private static ServiceResult? ValidateServiceSymbol(
        INamedTypeSymbol interfaceSymbol,
        ServiceBuildContext buildContext,
        CancellationToken ct,
        out (string Source, bool IsError) obsoleteAttribute)
    {
        obsoleteAttribute = default;
        if (ValidateInterfaceSymbol(interfaceSymbol, buildContext) is { } rejectedInterface)
        {
            return rejectedInterface;
        }

        obsoleteAttribute = BuildObsoleteAttribute(interfaceSymbol, ct);
        if (!obsoleteAttribute.IsError)
        {
            return null;
        }

        return RejectedService(
            buildContext.DisplayName,
            "[Obsolete(..., true)] service interfaces are not supported because generated proxy, dispatcher, and registration code must reference the service type",
            buildContext.ServiceLocation,
            buildContext.QualifiedInterfaceName);
    }

    private static ServiceResult RejectedService(
        string displayName,
        string reason,
        DiagnosticLocation location,
        string qualifiedInterfaceName) =>
        new(
            Model: null,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
            PropertyLocations: EquatableArray<DiagnosticLocation>.Empty,
            ServiceLocation: location,
            QualifiedInterfaceName: qualifiedInterfaceName,
            ServiceDiagnostic: new ServiceDiagnostic(displayName, reason, location));

    private static string? GetConfiguredServiceName(AttributeData serviceAttribute)
    {
        foreach (var namedArg in serviceAttribute.NamedArguments)
        {
            if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
            {
                return s;
            }
        }

        return null;
    }

    private static (string Source, bool IsError) BuildObsoleteAttribute(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken ct)
    {
        foreach (var attr in interfaceSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (attr.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute")
            {
                return ObsoleteAttributeFormatter.Format(attr);
            }
        }

        return (string.Empty, false);
    }

    private static string GetNamespace(INamespaceSymbol namespaceSymbol)
    {
        if (namespaceSymbol.IsGlobalNamespace)
        {
            return string.Empty;
        }

        var parts = new Stack<string>();
        for (var current = namespaceSymbol; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        {
            parts.Push(current.Name);
        }

        return string.Join(".", parts);
    }
}
