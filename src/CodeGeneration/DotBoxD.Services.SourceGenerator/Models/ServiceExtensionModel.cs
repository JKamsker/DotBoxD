using System;
using System.Collections.Generic;
using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Models;

/// <summary>
/// Method-insensitive shape used by the aggregate extension generator. A method rename should
/// regenerate the per-service proxy/dispatcher and metadata, but not the peer extension helpers.
/// </summary>
internal sealed record ServiceExtensionModel(
    string Namespace,
    string InterfaceName,
    string ServiceName,
    string ObsoleteAttribute,
    string ExperimentalDiagnosticId,
    EquatableArray<ServicePropertyModel> Properties,
    EquatableArray<SubServiceInfo> MethodSubServices)
{
    public static ServiceExtensionModel From(ServiceModel service)
    {
        var methodSubServices = new List<SubServiceInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in service.Methods.Array)
        {
            if (method.UnsupportedReason is not null || method.SubService is null)
            {
                continue;
            }

            var subService = method.SubService;
            if (seen.Add(subService.QualifiedInterfaceName))
            {
                methodSubServices.Add(subService);
            }
        }

        return new(
            service.Namespace,
            service.InterfaceName,
            service.ServiceName,
            service.ObsoleteAttribute,
            service.ExperimentalDiagnosticId,
            service.Properties,
            methodSubServices.ToEquatableArray());
    }
}
