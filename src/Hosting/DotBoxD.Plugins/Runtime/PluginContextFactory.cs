namespace DotBoxD.Plugins.Runtime;

internal static class PluginContextFactory
{
    public static Func<HookContext, HookContext> Identity { get; } = static context => context;

    public static Func<HookContext, TContext> Require<TContext>(
        Func<HookContext, TContext> factory,
        string name)
    {
        ArgumentNullException.ThrowIfNull(factory, name);
        return factory;
    }
}
