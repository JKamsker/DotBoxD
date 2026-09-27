namespace DotBoxD.Hosting;

internal sealed class WorkerPreparedPlanCache : IDisposable
{
    internal const int Capacity = 128;

    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _plans = new();
    private readonly LinkedList<Entry> _recency = new();
    private bool _disposed;

    internal bool TryGet(ExecutionPlan source, out ExecutionPlan prepared)
    {
        lock (_gate)
        {
            if (_plans.TryGetValue(CacheKey.Create(source), out var node))
            {
                _recency.Remove(node);
                _recency.AddLast(node);
                prepared = node.Value.Plan;
                return true;
            }

            prepared = null!;
            return false;
        }
    }

    internal void TryAdd(ExecutionPlan source, ExecutionPlan prepared)
    {
        var key = CacheKey.Create(source);
        lock (_gate)
        {
            // Preparation can finish after disposal; never retain that late result.
            if (_disposed || _plans.ContainsKey(key))
            {
                return;
            }

            var node = _recency.AddLast(new Entry(key, prepared));
            _plans.Add(key, node);
            if (_plans.Count > Capacity)
            {
                var oldest = _recency.First!;
                _recency.RemoveFirst();
                _plans.Remove(oldest.Value.Key);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _plans.Clear();
            _recency.Clear();
        }
    }

    private readonly record struct Entry(CacheKey Key, ExecutionPlan Plan);

    // Preserve value equality across independently prepared equivalent requests, including all
    // metadata dimensions. A shared seal alone does not identify the requested module and policy.
    private readonly record struct CacheKey(
        ExecutionPlanSeal PlanSeal,
        string ModuleHash,
        string PlanHash,
        string PolicyHash,
        string BindingManifestHash)
    {
        internal static CacheKey Create(ExecutionPlan plan)
            => new(plan.PlanSeal, plan.ModuleHash, plan.PlanHash, plan.PolicyHash, plan.BindingManifestHash);
    }
}
