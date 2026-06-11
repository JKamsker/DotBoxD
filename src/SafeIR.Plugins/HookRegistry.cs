namespace SafeIR.Plugins;

public sealed class HookRegistry
{
    private readonly Dictionary<Type, object> _pipelines = [];
    private readonly IPluginMessageSink _messages;

    internal HookRegistry(IPluginMessageSink messages)
    {
        _messages = messages;
    }

    public HookPipeline<TEvent> On<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        if (_pipelines.TryGetValue(typeof(TEvent), out var existing)) {
            return (HookPipeline<TEvent>)existing;
        }

        var pipeline = new HookPipeline<TEvent>(adapter, _messages);
        _pipelines[typeof(TEvent)] = pipeline;
        return pipeline;
    }

    public async ValueTask PublishAsync<TEvent>(TEvent e, CancellationToken cancellationToken = default)
    {
        if (_pipelines.TryGetValue(typeof(TEvent), out var pipeline)) {
            await ((HookPipeline<TEvent>)pipeline).PublishAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class HookPipeline<TEvent>
{
    private readonly List<Func<TEvent, HookContext, ValueTask<bool>>> _filters = [];
    private readonly List<Func<TEvent, HookContext, ValueTask>> _handlers = [];
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly IPluginMessageSink _messages;

    internal HookPipeline(IPluginEventAdapter<TEvent> adapter, IPluginMessageSink messages)
    {
        _adapter = adapter;
        _messages = messages;
    }

    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
        => Where((e, context) => ValueTask.FromResult(filter(e, context)));

    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, ValueTask<bool>> filter)
    {
        _filters.Add(filter);
        return this;
    }

    public HookPipeline<TEvent> InvokeKernel(Func<TEvent, HookContext, ValueTask> handler)
    {
        _handlers.Add(handler);
        return this;
    }

    public HookPipeline<TEvent> InvokeKernel(Action<TEvent, HookContext> handler)
        => InvokeKernel((e, context) => {
            handler(e, context);
            return ValueTask.CompletedTask;
        });

    public HookPipeline<TEvent> UseKernel(InstalledKernel kernel)
    {
        kernel.ValidateFor(_adapter);
        _handlers.Add(async (e, context) => {
            if (await kernel.ShouldHandleAsync(_adapter, e, context.CancellationToken).ConfigureAwait(false)) {
                await kernel.HandleAsync(_adapter, e, context.CancellationToken).ConfigureAwait(false);
            }
        });
        return this;
    }

    internal async ValueTask PublishAsync(TEvent e, CancellationToken cancellationToken)
    {
        var context = new HookContext(_messages, cancellationToken);
        foreach (var filter in _filters) {
            if (!await filter(e, context).ConfigureAwait(false)) {
                return;
            }
        }

        foreach (var handler in _handlers) {
            await handler(e, context).ConfigureAwait(false);
        }
    }
}
