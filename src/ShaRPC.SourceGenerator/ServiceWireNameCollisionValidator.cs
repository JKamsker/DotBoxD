using System.Collections.Immutable;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal sealed record ServiceWireNameIndex(EquatableArray<ServiceWireNameEntry> Entries)
{
    public static ServiceWireNameIndex Create(ImmutableArray<ServiceResult> results, CancellationToken ct)
    {
        var entries = ImmutableArray.CreateBuilder<ServiceWireNameEntry>();
        foreach (var result in results)
        {
            ct.ThrowIfCancellationRequested();

            if (result.Model is null)
            {
                continue;
            }

            entries.Add(new ServiceWireNameEntry(
                result.Model.ServiceName,
                result.QualifiedInterfaceName));
        }

        return new ServiceWireNameIndex(entries.ToImmutable().ToEquatableArray());
    }

    public ServiceWireNameEntry? FindCollision(ServiceModel model, string qualifiedInterfaceName, CancellationToken ct)
    {
        foreach (var entry in Entries.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.ServiceName == model.ServiceName &&
                entry.QualifiedInterfaceName != qualifiedInterfaceName)
            {
                return entry;
            }
        }

        return null;
    }
}

internal readonly record struct ServiceWireNameEntry(
    string ServiceName,
    string QualifiedInterfaceName);

internal static class ServiceWireNameCollisionValidator
{
    public static ServiceResult Apply(
        ServiceResult result,
        ServiceWireNameIndex index,
        CancellationToken ct)
    {
        if (result.Model is null)
        {
            return result;
        }

        var collision = index.FindCollision(result.Model, result.QualifiedInterfaceName, ct);
        if (collision is null)
        {
            return result;
        }

        var reason =
            $"wire service name '{result.Model.ServiceName}' is used by multiple services; give each service a distinct [ShaRpcService(Name = ...)] value";

        return new ServiceResult(
            Model: null,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
            ServiceLocation: result.ServiceLocation,
            QualifiedInterfaceName: result.QualifiedInterfaceName,
            ServiceDiagnostic: new ServiceDiagnostic(
                GetDisplayName(result.Model),
                reason,
                result.ServiceLocation));
    }

    private static string GetDisplayName(ServiceModel model) =>
        string.IsNullOrEmpty(model.Namespace)
            ? IdentifierHelpers.EscapeIdentifier(model.InterfaceName)
            : IdentifierHelpers.EscapeNamespace(model.Namespace) + "." +
                IdentifierHelpers.EscapeIdentifier(model.InterfaceName);
}
