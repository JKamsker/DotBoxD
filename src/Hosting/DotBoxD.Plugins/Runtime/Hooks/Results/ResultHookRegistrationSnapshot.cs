namespace DotBoxD.Plugins.Runtime.Hooks;

internal sealed class ResultHookRegistrationSnapshot<TEvent>
{
    private ResultHookRegistrationSnapshot(
        object entries,
        IResultHookRegistration<TEvent>[] registrations)
    {
        Entries = entries;
        Registrations = registrations;
    }

    public IResultHookRegistration<TEvent>[] Registrations { get; }

    public bool IsFor(object entries) => ReferenceEquals(Entries, entries);

    public static ResultHookRegistrationSnapshot<TEvent> Create<TContext>(
        HookPipeline<TEvent, TContext> owner,
        ResultHookSlot<TEvent, TContext>.Entry[] entries)
    {
        // The slot publishes entry arrays by replacement. Wrappers can therefore live exactly as long as this
        // array identity remains current without observing in-place mutation.
        if (entries.Length == 0)
        {
            return new ResultHookRegistrationSnapshot<TEvent>(entries, []);
        }

        var registrations = new IResultHookRegistration<TEvent>[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            registrations[i] = new ResultHookRegistration<TEvent, TContext>(owner, entries[i]);
        }

        return new ResultHookRegistrationSnapshot<TEvent>(entries, registrations);
    }

    private object Entries { get; }
}
