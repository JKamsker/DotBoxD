using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class AsyncSiblingProjector
{
    private static List<AsyncSiblingMethod> CollectCandidates(
        ServiceModel service,
        EquatableArray<DiagnosticLocation> methodLocations,
        IReadOnlyDictionary<string, string> blockedSignatures,
        IReadOnlyDictionary<string, string> originalSignatures,
        List<MethodDiagnostic> collisions,
        CancellationToken ct)
    {
        var candidates = new List<AsyncSiblingMethod>();
        for (var i = 0; i < service.Methods.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (TryCreateCandidate(
                    service,
                    i,
                    methodLocations,
                    blockedSignatures,
                    originalSignatures,
                    collisions,
                    ct) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static AsyncSiblingMethod? TryCreateCandidate(
        ServiceModel service,
        int sourceIndex,
        EquatableArray<DiagnosticLocation> methodLocations,
        IReadOnlyDictionary<string, string> blockedSignatures,
        IReadOnlyDictionary<string, string> originalSignatures,
        List<MethodDiagnostic> collisions,
        CancellationToken ct)
    {
        var method = service.Methods[sourceIndex];
        if (method.UnsupportedReason is not null)
        {
            return null;
        }

        var siblingName = NamingHelpers.IsAsync(method.ReturnKind)
            ? method.Name
            : NamingHelpers.AsyncSiblingMethodName(method.Name);
        var siblingParameters = BuildAsyncSiblingParameters(method, ct);
        var requiresExtra = RequiresExtraProxyMethod(method, siblingName, siblingParameters, ct);
        var candidateKey = SignatureKey(siblingName, method.TypeParameterCount, siblingParameters, ct);
        if (requiresExtra &&
            (TryAddSignatureCollision(
                    service,
                    method,
                    sourceIndex,
                    methodLocations,
                    blockedSignatures,
                    candidateKey,
                    siblingName,
                    unsupported: true,
                    collisions) ||
                TryAddSignatureCollision(
                    service,
                    method,
                    sourceIndex,
                    methodLocations,
                    originalSignatures,
                    candidateKey,
                    siblingName,
                    unsupported: false,
                    collisions)))
        {
            return null;
        }

        return new AsyncSiblingMethod(
            sourceIndex,
            siblingName,
            method,
            ProjectedReturnKind(method.ReturnKind),
            siblingParameters,
            requiresExtra);
    }

    private static bool RequiresExtraProxyMethod(
        MethodModel method,
        string siblingName,
        EquatableArray<ParameterModel> siblingParameters,
        CancellationToken ct)
        => !(siblingName == method.Name &&
             ParametersEqual(method.Parameters, siblingParameters, ct) &&
             NamingHelpers.IsAsync(method.ReturnKind));

    private static bool TryAddSignatureCollision(
        ServiceModel service,
        MethodModel method,
        int sourceIndex,
        EquatableArray<DiagnosticLocation> methodLocations,
        IReadOnlyDictionary<string, string> signatures,
        string candidateKey,
        string siblingName,
        bool unsupported,
        List<MethodDiagnostic> collisions)
    {
        if (!signatures.TryGetValue(candidateKey, out var blockerName))
        {
            return false;
        }

        var blocker = unsupported ? $"unsupported method '{blockerName}'" : $"method '{blockerName}'";
        collisions.Add(new MethodDiagnostic(
            service.InterfaceName,
            method.Name,
            $"the async-sibling projection '{siblingName}' would collide with {blocker}. Rename one of the methods or drop the trailing 'Async' on the sync method.",
            GetLocation(sourceIndex, methodLocations)));
        return true;
    }

    private static MethodReturnKind ProjectedReturnKind(MethodReturnKind kind)
        => kind switch
        {
            MethodReturnKind.Void => MethodReturnKind.Task,
            MethodReturnKind.Sync => MethodReturnKind.TaskOf,
            MethodReturnKind.SyncSubService => MethodReturnKind.TaskOfSubService,
            MethodReturnKind.Stream => MethodReturnKind.TaskOfStream,
            MethodReturnKind.Pipe => MethodReturnKind.TaskOfPipe,
            _ => kind,
        };

    private static List<AsyncSiblingMethod> ResolveCandidateGroups(
        ServiceModel service,
        EquatableArray<DiagnosticLocation> methodLocations,
        IReadOnlyList<AsyncSiblingMethod> candidates,
        List<MethodDiagnostic> collisions,
        CancellationToken ct)
    {
        var groups = GroupCandidates(candidates, ct);
        var rows = new List<AsyncSiblingMethod>();
        var handledKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var key = SignatureKey(candidate.Name, candidate.Source.TypeParameterCount, candidate.Parameters, ct);
            if (handledKeys.Add(key))
            {
                AddResolvedGroup(service, methodLocations, groups[key], rows, collisions, ct);
            }
        }

        return rows;
    }

    private static Dictionary<string, List<AsyncSiblingMethod>> GroupCandidates(
        IReadOnlyList<AsyncSiblingMethod> candidates,
        CancellationToken ct)
    {
        var groups = new Dictionary<string, List<AsyncSiblingMethod>>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var key = SignatureKey(candidate.Name, candidate.Source.TypeParameterCount, candidate.Parameters, ct);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<AsyncSiblingMethod>();
                groups[key] = group;
            }

            group.Add(candidate);
        }

        return groups;
    }

    private static void AddResolvedGroup(
        ServiceModel service,
        EquatableArray<DiagnosticLocation> methodLocations,
        IReadOnlyList<AsyncSiblingMethod> group,
        List<AsyncSiblingMethod> rows,
        List<MethodDiagnostic> collisions,
        CancellationToken ct)
    {
        if (group.Count == 1)
        {
            rows.Add(group[0]);
            return;
        }

        var keeper = group.FirstOrDefault(static row => !row.RequiresExtraProxyMethod);
        if (keeper is not null)
        {
            rows.Add(keeper);
        }

        AddGroupCollisions(service, methodLocations, group, keeper, collisions, ct);
    }

    private static void AddGroupCollisions(
        ServiceModel service,
        EquatableArray<DiagnosticLocation> methodLocations,
        IReadOnlyList<AsyncSiblingMethod> group,
        AsyncSiblingMethod? keeper,
        List<MethodDiagnostic> collisions,
        CancellationToken ct)
    {
        foreach (var row in group)
        {
            ct.ThrowIfCancellationRequested();
            if (ReferenceEquals(row, keeper))
            {
                continue;
            }

            var other = group.First(candidateRow => !ReferenceEquals(candidateRow, row));
            collisions.Add(new MethodDiagnostic(
                service.InterfaceName,
                row.Source.Name,
                $"the async-sibling projection '{row.Name}' would collide with '{other.Source.Name}'. Rename one of the methods or drop the trailing 'Async' on the sync method.",
                GetLocation(row.SourceIndex, methodLocations)));
        }
    }
}
