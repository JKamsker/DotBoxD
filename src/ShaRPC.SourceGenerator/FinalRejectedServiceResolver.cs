using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal static class FinalRejectedServiceResolver
{
    public static RejectedServiceIndex Resolve(
        ImmutableArray<ServiceResult> baseResults,
        ExistingTypeIndex existingTypes,
        CancellationToken ct)
    {
        var rejected = CreateRejectedServices(baseResults, ct);
        var seen = new List<RejectedServiceIndex>();

        for (var iteration = 0; iteration <= baseResults.Length + 1; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            var next = ComputeNext(baseResults, rejected, existingTypes, ct);
            if (SameRejectedServices(rejected, next, ct))
            {
                return next;
            }

            var cycleStart = IndexOf(seen, next, ct);
            if (cycleStart >= 0)
            {
                return Union(seen, cycleStart, next, ct);
            }

            seen.Add(rejected);
            rejected = next;
        }

        return rejected;
    }

    private static RejectedServiceIndex ComputeNext(
        ImmutableArray<ServiceResult> baseResults,
        RejectedServiceIndex rejected,
        ExistingTypeIndex existingTypes,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<ServiceResult>(baseResults.Length);
        foreach (var result in baseResults)
        {
            ct.ThrowIfCancellationRequested();

            var withSubServiceStubs = SubServiceAvailabilityValidator.Apply(result, rejected, ct);
            builder.Add(GeneratedTypeCollisionValidator.ApplyAsyncSibling(
                withSubServiceStubs,
                existingTypes,
                ct));
        }

        return CreateRejectedServices(builder.ToImmutable(), ct);
    }

    private static RejectedServiceIndex CreateRejectedServices(
        ImmutableArray<ServiceResult> results,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<RejectedServiceIdentity>();
        foreach (var result in results)
        {
            ct.ThrowIfCancellationRequested();

            var rejected = RejectedServiceIdentity.From(result);
            if (rejected is not null)
            {
                builder.Add(rejected.Value);
            }
        }

        return RejectedServiceIndex.Create(builder.ToImmutable(), ct);
    }

    private static int IndexOf(
        List<RejectedServiceIndex> seen,
        RejectedServiceIndex target,
        CancellationToken ct)
    {
        for (var i = 0; i < seen.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (SameRejectedServices(seen[i], target, ct))
            {
                return i;
            }
        }

        return -1;
    }

    private static RejectedServiceIndex Union(
        List<RejectedServiceIndex> seen,
        int cycleStart,
        RejectedServiceIndex repeated,
        CancellationToken ct)
    {
        var identities = ImmutableArray.CreateBuilder<RejectedServiceIdentity>();
        for (var i = cycleStart; i < seen.Count; i++)
        {
            AddAll(identities, seen[i], ct);
        }

        AddAll(identities, repeated, ct);
        return RejectedServiceIndex.Create(identities.ToImmutable(), ct);
    }

    private static void AddAll(
        ImmutableArray<RejectedServiceIdentity>.Builder identities,
        RejectedServiceIndex index,
        CancellationToken ct)
    {
        foreach (var qualifiedName in index.QualifiedInterfaceNames.Array)
        {
            ct.ThrowIfCancellationRequested();
            identities.Add(new RejectedServiceIdentity(qualifiedName));
        }
    }

    private static bool SameRejectedServices(
        RejectedServiceIndex left,
        RejectedServiceIndex right,
        CancellationToken ct)
    {
        if (left.QualifiedInterfaceNames.Count != right.QualifiedInterfaceNames.Count)
        {
            return false;
        }

        for (var i = 0; i < left.QualifiedInterfaceNames.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (left.QualifiedInterfaceNames[i] != right.QualifiedInterfaceNames[i])
            {
                return false;
            }
        }

        return true;
    }
}
