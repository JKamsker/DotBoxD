using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Indexing;
using KernelParameter = DotBoxD.Kernels.Parameter;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed partial class EventIndexCancellationTests
{
    [Fact]
    public async Task DrainAsync_waits_for_publish_admitted_before_dispatch_tracking()
    {
        var getterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGetter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var server = PluginServer.Create(
            configureHost: builder => builder.AddBinding(BlockingRecordBinding(handlerEntered, releaseHandler)),
            defaultPolicy: ChainPolicy(allowRuntimeAsync: true));
        var adapter = new DrainBlockingEventAdapter();
        server.RegisterEventAdapter(adapter);
        var package = DrainBlockingPackage();
        var kernel = await server.InstallAsync(package, ChainPolicy(allowRuntimeAsync: true));
        var subscription = Assert.Single(package.Manifest.Subscriptions);
        var registry = new EventIndexRegistry();
        Assert.True(registry.Register(
            adapter,
            kernel,
            subscription.IndexedPredicates,
            subscription.IndexCoversPredicate));

        var publish = Task.Run(() => registry.Publish(new DrainBlockingEvent(getterEntered, releaseGetter)));
        await getterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var drain = registry.DrainAsync();

        try
        {
            await AssertNotCompletedAsync(
                drain,
                "DrainAsync completed while Publish was blocked before dispatch tracking.");

            releaseGetter.SetResult();
            await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await publish.WaitAsync(TimeSpan.FromSeconds(5));

            await AssertNotCompletedAsync(
                drain,
                "DrainAsync completed while the dispatched handler was still in flight.");

            releaseHandler.SetResult();
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseGetter.TrySetResult();
            releaseHandler.TrySetResult();
            await Task.WhenAny(publish, Task.Delay(TimeSpan.FromSeconds(1)));
            await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(1)));
        }

        Assert.Equal(1, registry.Stats.Considered);
        Assert.Equal(0, registry.Stats.Prefiltered);
        Assert.Equal(1, registry.Stats.Dispatched);
    }

    private static PluginPackage DrainBlockingPackage()
    {
        const string pluginId = "indexed-drain-concurrency";
        var eventName = typeof(DrainBlockingEvent).FullName!;
        var manifest = new PluginManifest(
            pluginId,
            $"IEventKernel<{eventName}>",
            ExecutionMode.Auto,
            ["Cpu", "Alloc", "Concurrency"],
            [],
            [
                new HookSubscriptionManifest(eventName, pluginId)
                {
                    IndexedPredicates =
                    [
                        new IndexedPredicate(
                            nameof(DrainBlockingEvent.AttackerId),
                            IndexPredicateOperator.Equals,
                            "player-1",
                            "string")
                    ]
                }
            ])
        {
            RequiredCapabilities = ["dotboxd.runtime.async"]
        };
        var module = new SandboxModule(
            pluginId,
            SemVersion.One,
            SemVersion.One,
            [],
            [DrainBlockingShouldHandle(), DrainBlockingHandle()],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kernel"] = pluginId,
                ["pluginId"] = pluginId
            });
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    private static SandboxFunction DrainBlockingShouldHandle()
    {
        var predicate = new BinaryExpression(
            new VariableExpression("e_AttackerId", Span),
            "==",
            new LiteralExpression(SandboxValue.FromString("player-1"), Span),
            Span);

        return new SandboxFunction(
            "ShouldHandle",
            true,
            DrainBlockingEventParameters(),
            SandboxType.Bool,
            [new ReturnStatement(predicate, Span)]);
    }

    private static SandboxFunction DrainBlockingHandle()
        => new(
            "Handle",
            true,
            DrainBlockingEventParameters(),
            SandboxType.Unit,
            [
                new ExpressionStatement(new CallExpression("test.block", [], null, Span), Span),
                new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)
            ]);

    private static Parameter[] DrainBlockingEventParameters()
        =>
        [
            new("e_AttackerId", SandboxType.String)
        ];

    private static BindingDescriptor BlockingRecordBinding(
        TaskCompletionSource handlerEntered,
        TaskCompletionSource releaseHandler)
        => new BindingDescriptor(
            "test.block",
            SemVersion.One,
            [],
            SandboxType.Bool,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            async (_, _, cancellationToken) =>
            {
                handlerEntered.TrySetResult();
                await releaseHandler.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return SandboxValue.FromBool(true);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        with
        {
            IsAsync = true
        };

    private static async Task AssertNotCompletedAsync(Task task, string message)
    {
        var delay = Task.Delay(TimeSpan.FromMilliseconds(150));
        var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
        Assert.False(ReferenceEquals(task, completed), message);
    }

    private sealed class DrainBlockingEvent(
        TaskCompletionSource getterEntered,
        TaskCompletionSource releaseGetter)
    {
        [EventIndexKey]
        public string AttackerId
        {
            get
            {
                getterEntered.TrySetResult();
                releaseGetter.Task.GetAwaiter().GetResult();
                return "player-1";
            }
        }
    }

    private sealed class DrainBlockingEventAdapter : IPluginEventAdapter<DrainBlockingEvent>
    {
        public string EventName => typeof(DrainBlockingEvent).FullName!;

        public IReadOnlyList<KernelParameter> Parameters { get; } =
        [
            new("e_AttackerId", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DrainBlockingEvent e) =>
        [
            SandboxValue.FromString("player-1")
        ];
    }
}
