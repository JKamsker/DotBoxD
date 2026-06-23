using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime;

internal sealed class ServerContextFactory<TContext>
{
    private readonly Func<HookContext, TContext> _create;

    public ServerContextFactory(Func<HookContext, TContext> create)
        => _create = create ?? throw new ArgumentNullException(nameof(create));

    public TContext Create(HookContext context) => _create(context);

    public bool Uses(Func<HookContext, TContext> create)
        => _create == create;

    internal static HookContext Identity(HookContext context) => context;

    public static ServerContextFactory<HookContext> Default { get; } = new(Identity);
}

internal interface IPluginEventPipelineRegistry
{
    void EnsureCanRegister<TEvent>(IPluginEventAdapter<TEvent> adapter);
    void RemoveKernel(InstalledKernel kernel);
    void RemoveKernelPool(InstalledKernelPool pool);
}
