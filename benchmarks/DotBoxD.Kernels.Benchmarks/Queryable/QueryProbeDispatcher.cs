using DotBoxD.Kernels.Benchmarks.Core.Collections;

namespace DotBoxD.Kernels.Benchmarks.Queryable;

internal static class QueryProbeDispatcher
{
    public static bool TryRun(string[] args)
    {
        if (args.Contains("--probe-query-specialization", StringComparer.OrdinalIgnoreCase))
        {
            QuerySpecializationProbe.Run();
        }
        else if (args.Contains("--probe-query-membership", StringComparer.OrdinalIgnoreCase))
        {
            QuerySpecializationProbe.RunMembership();
        }
        else if (args.Contains("--probe-query-membership-linear", StringComparer.OrdinalIgnoreCase))
        {
            QuerySpecializationProbe.RunMembership(prepare: false);
        }
        else if (args.Contains("--probe-query-churn", StringComparer.OrdinalIgnoreCase))
        {
            QuerySubscriptionChurnProbe.Run();
        }
        else if (args.Contains("--probe-primitive-packing", StringComparer.OrdinalIgnoreCase))
        {
            PrimitivePackingProbe.Run();
        }
        else if (args.Contains("--probe-event-query-dispatch", StringComparer.OrdinalIgnoreCase))
        {
            EventQueryDispatchProbe.Run();
        }
        else
        {
            return false;
        }

        return true;
    }
}
