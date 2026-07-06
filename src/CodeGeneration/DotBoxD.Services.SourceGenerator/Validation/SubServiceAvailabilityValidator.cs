using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class ServiceAvailabilityIndexHelpers
{
    public static List<string> SortedUnique(List<string> names, CancellationToken ct)
    {
        names.Sort((left, right) =>
        {
            ct.ThrowIfCancellationRequested();
            return string.Compare(left, right, System.StringComparison.Ordinal);
        });

        var uniqueNames = new List<string>(names.Count);
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            if (uniqueNames.Count == 0 || uniqueNames[uniqueNames.Count - 1] != name)
            {
                uniqueNames.Add(name);
            }
        }

        return uniqueNames;
    }

    public static bool ContainsSorted(
        EquatableArray<string> qualifiedInterfaceNames,
        string qualifiedInterfaceName,
        CancellationToken ct)
    {
        var low = 0;
        var high = qualifiedInterfaceNames.Count - 1;
        while (low <= high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = low + ((high - low) / 2);
            var comparison = string.Compare(
                qualifiedInterfaceNames[mid],
                qualifiedInterfaceName,
                System.StringComparison.Ordinal);
            if (comparison == 0)
            {
                return true;
            }

            if (comparison < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return false;
    }
}

internal sealed record RejectedServiceIndex(EquatableArray<string> QualifiedInterfaceNames)
{
    public static RejectedServiceIndex Create(ImmutableArray<RejectedServiceIdentity> services, CancellationToken ct)
    {
        var names = new List<string>(services.Length);
        foreach (var service in services)
        {
            ct.ThrowIfCancellationRequested();
            names.Add(service.QualifiedInterfaceName);
        }

        return new RejectedServiceIndex(ServiceAvailabilityIndexHelpers.SortedUnique(names, ct).ToEquatableArray());
    }

    public bool Contains(string qualifiedInterfaceName, CancellationToken ct)
        => ServiceAvailabilityIndexHelpers.ContainsSorted(QualifiedInterfaceNames, qualifiedInterfaceName, ct);
}

internal static class SubServiceAvailabilityValidator
{
    public static ServiceResult Apply(
        ServiceResult result,
        GeneratedServiceIndex generatedServices,
        RejectedServiceIndex rejectedServices,
        CancellationToken ct)
    {
        var model = result.Model;
        if (model is null)
        {
            return result;
        }

        if (TryRejectUnavailableProperty(result, model, generatedServices, rejectedServices, ct, out var rejectedResult))
        {
            return rejectedResult;
        }

        var methodUpdate = ApplyMethodAvailability(model, result.MethodLocations, result.MethodDiagnostics, generatedServices, rejectedServices, ct);
        if (!methodUpdate.Changed)
        {
            return result;
        }

        return result with
        {
            Model = model with { Methods = methodUpdate.Methods.ToEquatableArray() },
            MethodDiagnostics = methodUpdate.Diagnostics.ToEquatableArray(),
        };
    }

    private static bool TryRejectUnavailableProperty(
        ServiceResult result,
        ServiceModel model,
        GeneratedServiceIndex generatedServices,
        RejectedServiceIndex rejectedServices,
        CancellationToken ct,
        out ServiceResult rejectedResult)
    {
        rejectedResult = default;
        for (var i = 0; i < model.Properties.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var property = model.Properties[i];
            if (property.SubService is null)
            {
                continue;
            }

            if (IsUnavailable(property.SubService, generatedServices, rejectedServices, ct))
            {
                rejectedResult = RejectUnavailableProperty(result, model, property, i);
                return true;
            }
        }

        return false;
    }

    private static ServiceResult RejectUnavailableProperty(
        ServiceResult result,
        ServiceModel model,
        ServicePropertyModel property,
        int propertyIndex)
    {
        var reason =
            $"sub-service property '{IdentifierHelpers.UnescapeIdentifier(property.Name)}' cannot be proxied because that service was not generated";
        return result with
        {
            Model = null,
            MethodDiagnostics = EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations = EquatableArray<DiagnosticLocation>.Empty,
            PropertyLocations = EquatableArray<DiagnosticLocation>.Empty,
            ServiceDiagnostic = new ServiceDiagnostic(
                GetDisplayName(model),
                reason,
                GetLocation(result.PropertyLocations, propertyIndex)),
        };
    }

    private static (List<MethodModel> Methods, List<MethodDiagnostic> Diagnostics, bool Changed) ApplyMethodAvailability(
        ServiceModel model,
        EquatableArray<DiagnosticLocation> methodLocations,
        EquatableArray<MethodDiagnostic> methodDiagnostics,
        GeneratedServiceIndex generatedServices,
        RejectedServiceIndex rejectedServices,
        CancellationToken ct)
    {
        var methods = new List<MethodModel>();
        var diagnostics = new List<MethodDiagnostic>(methodDiagnostics.Array);
        var changed = false;
        for (var i = 0; i < model.Methods.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var method = ApplyMethodAvailability(
                model,
                model.Methods[i],
                methodLocations,
                i,
                generatedServices,
                rejectedServices,
                ct,
                out var diagnostic);
            if (diagnostic is { } methodDiagnostic)
            {
                diagnostics.Add(methodDiagnostic);
                changed = true;
            }

            methods.Add(method);
        }

        return (methods, diagnostics, changed);
    }

    private static MethodModel ApplyMethodAvailability(
        ServiceModel model,
        MethodModel method,
        EquatableArray<DiagnosticLocation> methodLocations,
        int methodIndex,
        GeneratedServiceIndex generatedServices,
        RejectedServiceIndex rejectedServices,
        CancellationToken ct,
        out MethodDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (method.UnsupportedReason is not null)
        {
            return method;
        }

        if (method.SubService is null)
        {
            return method;
        }

        if (IsUnavailable(method.SubService, generatedServices, rejectedServices, ct))
        {
            var reason =
                $"sub-service return type '{method.SubService.QualifiedInterfaceName}' cannot be proxied because that service was not generated";
            return RejectMethod(model, method, methodLocations, methodIndex, reason, out diagnostic);
        }

        if (generatedServices.TryGetInstanceScopedSubServiceProperty(
                method.SubService.QualifiedInterfaceName,
                ct,
                out var propertyName))
        {
            var reason =
                $"sub-service return type '{method.SubService.QualifiedInterfaceName}' exposes sub-service property '{IdentifierHelpers.UnescapeIdentifier(propertyName)}' whose proxy would not be instance-scoped";
            return RejectMethod(model, method, methodLocations, methodIndex, reason, out diagnostic);
        }

        return method;
    }

    private static MethodModel RejectMethod(
        ServiceModel model,
        MethodModel method,
        EquatableArray<DiagnosticLocation> methodLocations,
        int methodIndex,
        string reason,
        out MethodDiagnostic? diagnostic)
    {
        diagnostic = new MethodDiagnostic(
            GetDisplayName(model),
            method.Name,
            reason,
            GetLocation(methodLocations, methodIndex));
        return method with { UnsupportedReason = reason };
    }

    private static DiagnosticLocation GetLocation(
        EquatableArray<DiagnosticLocation> locations,
        int index)
    {
        if (index < 0 || index >= locations.Count)
        {
            return default;
        }

        return locations[index];
    }

    private static string GetDisplayName(ServiceModel model) =>
        string.IsNullOrEmpty(model.Namespace)
            ? IdentifierHelpers.EscapeIdentifier(model.InterfaceName)
            : IdentifierHelpers.EscapeNamespace(model.Namespace) + "." +
                IdentifierHelpers.EscapeIdentifier(model.InterfaceName);

    private static bool IsUnavailable(
        SubServiceInfo subService,
        GeneratedServiceIndex generatedServices,
        RejectedServiceIndex rejectedServices,
        CancellationToken ct)
        => rejectedServices.Contains(subService.QualifiedInterfaceName, ct) ||
            (!subService.HasProxyCompanion &&
             !generatedServices.Contains(subService.QualifiedInterfaceName, ct));
}
