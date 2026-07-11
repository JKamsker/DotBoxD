using System;
using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class AsyncSiblingProjector
{
    public static (EquatableArray<AsyncSiblingMethod> Siblings, EquatableArray<MethodDiagnostic> Collisions)
        Compute(ServiceModel service, CancellationToken ct = default)
    {
        return Compute(service, EquatableArray<DiagnosticLocation>.Empty, ct);
    }

    public static (EquatableArray<AsyncSiblingMethod> Siblings, EquatableArray<MethodDiagnostic> Collisions)
        Compute(ServiceModel service, EquatableArray<DiagnosticLocation> methodLocations, CancellationToken ct)
    {
        var collisions = new List<MethodDiagnostic>();
        var blockedSignatures = UnsupportedOriginalSignatures(service, ct);
        var originalSignatures = OriginalSignatures(service, ct);
        var candidates = CollectCandidates(
            service,
            methodLocations,
            blockedSignatures,
            originalSignatures,
            collisions,
            ct);
        var rows = ResolveCandidateGroups(service, methodLocations, candidates, collisions, ct);

        return (rows.ToEquatableArray(), collisions.ToEquatableArray());
    }

    private static DiagnosticLocation GetLocation(
        int sourceIndex,
        EquatableArray<DiagnosticLocation> methodLocations)
    {
        if (sourceIndex < 0 || sourceIndex >= methodLocations.Count)
        {
            return default;
        }

        return methodLocations[sourceIndex];
    }

    private static Dictionary<string, string> UnsupportedOriginalSignatures(
        ServiceModel service,
        CancellationToken ct)
    {
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var method in service.Methods.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (method.UnsupportedReason is not null)
            {
                signatures[SignatureKey(method.Name, method.TypeParameterCount, method.Parameters, ct)] = method.Name;
            }
        }

        return signatures;
    }

    private static Dictionary<string, string> OriginalSignatures(
        ServiceModel service,
        CancellationToken ct)
    {
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var method in service.Methods.Array)
        {
            ct.ThrowIfCancellationRequested();

            signatures[SignatureKey(method.Name, method.TypeParameterCount, method.Parameters, ct)] = method.Name;
        }

        return signatures;
    }

    private static string SignatureKey(
        string methodName,
        int arity,
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct) =>
        MethodSignatureFacts.GetSignatureKey(methodName, arity, parameters, ct);

    private static EquatableArray<ParameterModel> BuildAsyncSiblingParameters(
        MethodModel method,
        CancellationToken ct)
    {
        if (NamingHelpers.IsAsync(method.ReturnKind) && method.HasCancellationToken)
        {
            return method.Parameters;
        }

        var parameters = new List<ParameterModel>();
        var cancellationTokenName = "ct";
        foreach (var parameter in method.Parameters.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (parameter.IsCancellationToken)
            {
                cancellationTokenName = parameter.Name;
            }
            else
            {
                parameters.Add(parameter);
            }
        }

        parameters.Add(new ParameterModel(
            UniqueParameterName(parameters, cancellationTokenName, ct),
            ServicesGeneratorTypeNames.GlobalCancellationToken,
            ServicesGeneratorTypeNames.GlobalCancellationToken,
            IsCancellationToken: true,
            HasDefaultValue: true,
            MetadataType: ServicesGeneratorTypeNames.GlobalCancellationToken));

        return parameters.ToEquatableArray();
    }

    private static string UniqueParameterName(
        IReadOnlyList<ParameterModel> parameters,
        string baseName,
        CancellationToken ct)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            ct.ThrowIfCancellationRequested();
            usedNames.Add(parameter.Name);
        }

        var candidate = baseName;
        var suffix = 1;
        while (usedNames.Contains(candidate))
        {
            ct.ThrowIfCancellationRequested();

            candidate = baseName + suffix;
            suffix++;
        }

        return candidate;
    }

    private static bool ParametersEqual(
        EquatableArray<ParameterModel> left,
        EquatableArray<ParameterModel> right,
        CancellationToken ct)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
