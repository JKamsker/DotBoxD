using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public sealed class HookPipeline<TEvent>
{
    private readonly object _gate = new();

    // Copy-on-write snapshots: publish reads these references without locking or allocating,
    // while mutation replaces the whole array under _gate. Reading each reference once at the
    // start of a publish preserves stable per-publish semantics, because installed delegates
    // never mutate an existing array in place.
    private volatile Func<TEvent, HookContext, ValueTask<bool>>[] _filters = [];
    private volatile HookHandler<TEvent>[] _handlers = [];
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly IPluginMessageSink _messages;
    private readonly HookContext _defaultContext;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;

    internal HookPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null)
    {
        _adapter = adapter;
        _messages = messages;
        _defaultContext = new HookContext(messages, CancellationToken.None);
        _kernels = kernels;
        _installer = installer;
    }

    /// <summary>
    /// Installs an analyzer-generated hook-chain package and wires it into this pipeline. Called by
    /// the generated interceptor that replaces an <c>InvokeKernel(lambda)</c> call site, so the lowered
    /// chain runs as verified IR instead of throwing. Blocks on install at setup time.
    /// </summary>
    public HookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_installer is null)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "DBXK063",
                    "this hook pipeline has no installer; create it from a PluginServer to use generated chains.")
            ]);
        }

        return UseKernel(_installer(package));
    }

    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
        => Where((e, context) => ValueTask.FromResult(filter(e, context)));

    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, ValueTask<bool>> filter)
    {
        lock (_gate)
        {
            _filters = [.. _filters, filter];
        }

        return this;
    }

    /// <summary>Element-only filter — the same as the (element, context) overload with the context
    /// discarded. Both arities are always available so a stage need not take the context it doesn't use.</summary>
    public HookPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public HookPipeline<TEvent> Where(Func<TEvent, ValueTask<bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public HookPipeline<TEvent> InvokeHostHandler(Func<TEvent, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _handlers = [.. _handlers, new HookHandler<TEvent>(null, handler)];
        }

        return this;
    }

    public HookPipeline<TEvent> InvokeHostHandler(Action<TEvent, HookContext> handler)
        => InvokeHostHandler((e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        });

    public HookPipeline<TEvent> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public HookPipeline<TEvent> InvokeHostHandler(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    /// <summary>Native host terminal — runs in-process (NOT sandboxed). Use sparingly.</summary>
    public HookPipeline<TEvent> InvokeLocal(Func<TEvent, HookContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> InvokeLocal(Action<TEvent, HookContext> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> InvokeLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> InvokeLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    /// <summary>Projects the flowing element to a new type for downstream Where/terminal stages.</summary>
    public HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    public HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((e, _) => projection(e));
    }

    /// <summary>
    /// The terminal the analyzer lowers to verified IR. It never runs as host code: un-lowered it
    /// throws, so plugin logic cannot accidentally execute unsandboxed.
    /// </summary>
    public HookPipeline<TEvent> InvokeKernel(Func<TEvent, HookContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> InvokeKernel(Action<TEvent, HookContext> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> InvokeKernel(Func<TEvent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> InvokeKernel(Action<TEvent> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> UseKernel(InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        kernel.ValidateFor(_adapter);
        lock (_gate)
        {
            _handlers = [
                .. _handlers,
                new HookHandler<TEvent>(
                    kernel,
                    (e, context) => kernel.InvokeAsync(_adapter, e, context.CancellationToken))
            ];
        }

        return this;
    }

    public HookPipeline<TEvent> UseKernel<TKernel>() where TKernel : class
        => UseKernel(_kernels.GetByKernelType<TKernel>());

    internal bool UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => ReferenceEquals(_adapter, adapter);

    internal ValueTask PublishAsync(TEvent e, CancellationToken cancellationToken)
    {
        // Read each copy-on-write reference once for a stable per-publish snapshot. No lock or
        // allocation: a concurrent mutation replaces the array reference rather than editing it.
        var filters = _filters;
        var handlers = _handlers;

        var context = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultContext;
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i](e, context);
            if (!filter.IsCompletedSuccessfully)
                return PublishAfterFilterAwaitAsync(filter, filters, handlers, e, context, i);

            if (!filter.Result)
                return ValueTask.CompletedTask;
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            var handler = handlers[i].Invoke(e, context);
            if (!handler.IsCompletedSuccessfully)
                return PublishAfterHandlerAwaitAsync(handler, handlers, e, context, i);
        }

        return ValueTask.CompletedTask;
    }

    internal void RemoveKernel(InstalledKernel kernel)
    {
        lock (_gate)
        {
            var handlers = _handlers;
            var retainedCount = 0;
            for (var i = 0; i < handlers.Length; i++)
            {
                if (!ReferenceEquals(handlers[i].Kernel, kernel))
                {
                    retainedCount++;
                }
            }

            if (retainedCount == handlers.Length)
            {
                return;
            }

            var retained = new HookHandler<TEvent>[retainedCount];
            var index = 0;
            for (var i = 0; i < handlers.Length; i++)
            {
                if (!ReferenceEquals(handlers[i].Kernel, kernel))
                {
                    retained[index] = handlers[i];
                    index++;
                }
            }

            _handlers = retained;
        }
    }

    private static async ValueTask PublishAfterFilterAwaitAsync(
        ValueTask<bool> pending,
        Func<TEvent, HookContext, ValueTask<bool>>[] filters,
        HookHandler<TEvent>[] handlers,
        TEvent e,
        HookContext context,
        int index)
    {
        if (!await pending.ConfigureAwait(false)) {
            return;
        }

        for (var i = index + 1; i < filters.Length; i++)
        {
            if (!await filters[i](e, context).ConfigureAwait(false)) {
                return;
            }
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            await handlers[i].Invoke(e, context).ConfigureAwait(false);
        }
    }

    private static async ValueTask PublishAfterHandlerAwaitAsync(
        ValueTask pending,
        HookHandler<TEvent>[] handlers,
        TEvent e,
        HookContext context,
        int index)
    {
        await pending.ConfigureAwait(false);
        for (var i = index + 1; i < handlers.Length; i++)
        {
            await handlers[i].Invoke(e, context).ConfigureAwait(false);
        }
    }

    private readonly record struct HookHandler<T>(
        InstalledKernel? Kernel,
        Func<T, HookContext, ValueTask> Invoke);
}
