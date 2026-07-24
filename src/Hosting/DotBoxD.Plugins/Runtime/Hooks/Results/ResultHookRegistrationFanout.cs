namespace DotBoxD.Plugins.Runtime.Hooks;

internal static class ResultHookRegistrationFanout
{
    public static IResultHookRegistration<TEvent>[] Ordered<TEvent>(CachedPipelineFanout pipelines)
    {
        // The fanout's pipeline array is immutable. Source snapshots change identity when a slot changes, so a
        // matching merged snapshot is safe to share while an in-flight dispatch keeps one stable old ordering.
        while (true)
        {
            var current = pipelines.ReadResultRegistrationCache();
            if (current is ResultHookRegistrationFanoutSnapshot<TEvent> cached && cached.Matches(pipelines))
            {
                return cached.Registrations;
            }

            var created = ResultHookRegistrationFanoutSnapshot<TEvent>.Create(pipelines);
            var observed = pipelines.CompareExchangeResultRegistrationCache(created, current);
            if (ReferenceEquals(observed, current))
            {
                return created.Registrations;
            }
        }
    }
}

internal sealed class ResultHookRegistrationFanoutSnapshot<TEvent>
{
    private ResultHookRegistrationFanoutSnapshot(
        ResultHookRegistrationSnapshot<TEvent>[] sources,
        IResultHookRegistration<TEvent>[] registrations)
    {
        Sources = sources;
        Registrations = registrations;
    }

    public IResultHookRegistration<TEvent>[] Registrations { get; }

    public static ResultHookRegistrationFanoutSnapshot<TEvent> Create(CachedPipelineFanout pipelines)
    {
        var sources = new ResultHookRegistrationSnapshot<TEvent>[pipelines.Count];
        var registrationCount = 0;
        for (var i = 0; i < pipelines.Count; i++)
        {
            var source = ((IHookPipeline<TEvent>)pipelines[i]).ResultRegistrations();
            sources[i] = source;
            registrationCount = checked(registrationCount + source.Registrations.Length);
        }

        var registrations = registrationCount == 0
            ? []
            : new IResultHookRegistration<TEvent>[registrationCount];
        var destinationIndex = 0;
        foreach (var source in sources)
        {
            source.Registrations.CopyTo(registrations, destinationIndex);
            destinationIndex += source.Registrations.Length;
        }

        if (registrations.Length > 1)
        {
            Array.Sort(registrations, ResultHookRegistrationComparer<TEvent>.Instance);
        }

        return new ResultHookRegistrationFanoutSnapshot<TEvent>(sources, registrations);
    }

    public bool Matches(CachedPipelineFanout pipelines)
    {
        if (pipelines.Count != Sources.Length)
        {
            return false;
        }

        for (var i = 0; i < Sources.Length; i++)
        {
            var current = ((IHookPipeline<TEvent>)pipelines[i]).ResultRegistrations();
            if (!ReferenceEquals(current, Sources[i]))
            {
                return false;
            }
        }

        return true;
    }

    private ResultHookRegistrationSnapshot<TEvent>[] Sources { get; }
}

internal sealed class ResultHookRegistrationComparer<TEvent> : IComparer<IResultHookRegistration<TEvent>>
{
    public static ResultHookRegistrationComparer<TEvent> Instance { get; } = new();

    public int Compare(IResultHookRegistration<TEvent>? left, IResultHookRegistration<TEvent>? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.Priority != right.Priority
            ? right.Priority.CompareTo(left.Priority)
            : left.Order.CompareTo(right.Order);
    }
}
