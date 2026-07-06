using System;
using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class ServiceShapeValidator
{
    private const string ExtensibleControlType = "DotBoxD.Abstractions.IExtensibleControl";
    private const string ServiceControlType = "DotBoxD.Abstractions.IServiceControl";

    public static UnsupportedMemberDiagnostic? GetUnsupportedMemberDiagnostic(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken ct)
    {
        foreach (var member in EnumerateInterfaceMembers(interfaceSymbol, ct))
        {
            ct.ThrowIfCancellationRequested();
            var diagnostic = GetUnsupportedMemberDiagnostic(member);
            if (diagnostic is not null)
            {
                return diagnostic;
            }
        }

        return null;
    }

    public static UnsupportedMemberDiagnostic? CollectMembers(
        INamedTypeSymbol interfaceSymbol,
        List<IMethodSymbol> methods,
        List<IPropertySymbol> properties,
        CancellationToken ct)
    {
        foreach (var member in EnumerateInterfaceMembers(interfaceSymbol, ct))
        {
            ct.ThrowIfCancellationRequested();
            var diagnostic = GetUnsupportedMemberDiagnostic(member);
            if (diagnostic is not null)
            {
                return diagnostic;
            }

            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                if (IsControlPlaneMethod(method))
                {
                    continue;
                }

                methods.Add(method);
            }
            else if (member is IPropertySymbol property)
            {
                if (IsControlPlaneProperty(property))
                {
                    continue;
                }

                properties.Add(property);
            }
        }

        return null;
    }

    private static UnsupportedMemberDiagnostic? GetUnsupportedMemberDiagnostic(ISymbol member)
        => member switch
        {
            IPropertySymbol property => GetUnsupportedPropertyMemberDiagnostic(property),
            IEventSymbol eventSymbol => CreateDiagnostic(
                eventSymbol,
                $"interface event '{eventSymbol.Name}' is not supported; DotBoxD services may declare methods only"),
            IMethodSymbol method => GetUnsupportedMethodDiagnostic(method),
            _ => null
        };

    private static UnsupportedMemberDiagnostic? GetUnsupportedPropertyMemberDiagnostic(IPropertySymbol property)
        => IsControlPlaneProperty(property)
            ? null
            : GetUnsupportedPropertyDiagnostic(property);

    private static UnsupportedMemberDiagnostic? GetUnsupportedMethodDiagnostic(IMethodSymbol method)
    {
        if (IsControlPlaneMethod(method))
        {
            return null;
        }

        if (method.MethodKind == MethodKind.Ordinary &&
            method.DeclaredAccessibility != Accessibility.Public)
        {
            return CreateDiagnostic(
                method,
                $"non-public interface method '{method.Name}' is not supported; DotBoxD services may declare public instance methods only");
        }

        if (method.MethodKind == MethodKind.Ordinary && method.IsStatic)
        {
            return CreateDiagnostic(
                method,
                $"static interface method '{method.Name}' is not supported; DotBoxD services may declare instance methods only");
        }

        if (!IsSupportedInterfaceMethodKind(method.MethodKind))
        {
            return CreateDiagnostic(
                method,
                $"interface member '{method.Name}' has unsupported method kind '{method.MethodKind}'");
        }

        return null;
    }

    private static UnsupportedMemberDiagnostic? GetUnsupportedPropertyDiagnostic(IPropertySymbol property)
    {
        if (property.IsIndexer || property.Parameters.Length != 0)
        {
            return CreateDiagnostic(
                property,
                $"interface indexer '{property.Name}' is not supported; DotBoxD service properties must be named public get-only sub-service controls");
        }

        if (!IsSupportedSubServicePropertyShape(property))
        {
            return CreateDiagnostic(
                property,
                $"interface property '{property.Name}' is not supported; DotBoxD service properties must be public get-only sub-service controls");
        }

        if (IsInstanceIdProperty(property))
        {
            return null;
        }

        if (!ReturnTypeClassifier.TryGetSubServiceInfo(property.Type, CancellationToken.None, out var subService))
        {
            return CreateDiagnostic(
                property,
                $"interface property '{property.Name}' is not supported; DotBoxD service properties must return a [RpcService] interface or be the string Id of an instance handle");
        }

        if (subService.AllowsNull)
        {
            return CreateDiagnostic(
                property,
                $"nullable sub-service property '{property.Name}' is not supported; use a method returning a nullable sub-service when absence must be represented");
        }

        return null;
    }

    private static bool IsSupportedInterfaceMethodKind(MethodKind kind)
        => kind is MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove;

    private static bool IsSupportedSubServicePropertyShape(IPropertySymbol property)
        => property.GetMethod is not null &&
           property.GetMethod.DeclaredAccessibility == Accessibility.Public &&
           !property.GetMethod.IsStatic &&
           property.Parameters.Length == 0 &&
           property.SetMethod is null;

    internal static bool IsInstanceIdProperty(IPropertySymbol property) =>
        string.Equals(property.Name, "Id", StringComparison.Ordinal) &&
        property.Type.SpecialType == SpecialType.System_String;

    private static bool IsControlPlaneMethod(IMethodSymbol method)
    {
        var containingType = method.ContainingType.ToDisplayString();
        return containingType == ExtensibleControlType || containingType == ServiceControlType;
    }

    private static bool IsControlPlaneProperty(IPropertySymbol property)
    {
        var containingType = property.ContainingType.ToDisplayString();
        return containingType == ExtensibleControlType || containingType == ServiceControlType;
    }

    private static IEnumerable<ISymbol> EnumerateInterfaceMembers(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var members = interfaceSymbol.GetMembers();
        ct.ThrowIfCancellationRequested();
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            yield return member;
        }

        ct.ThrowIfCancellationRequested();
        var baseInterfaces = interfaceSymbol.AllInterfaces;
        ct.ThrowIfCancellationRequested();
        foreach (var baseInterface in baseInterfaces)
        {
            ct.ThrowIfCancellationRequested();
            var baseMembers = baseInterface.GetMembers();
            ct.ThrowIfCancellationRequested();
            foreach (var member in baseMembers)
            {
                ct.ThrowIfCancellationRequested();
                yield return member;
            }
        }
    }

    private static UnsupportedMemberDiagnostic CreateDiagnostic(ISymbol symbol, string reason) =>
        new(reason, DiagnosticLocationFactory.FromSymbol(symbol));
}
