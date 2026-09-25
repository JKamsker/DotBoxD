using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

public sealed class KernelReplacementCancellationCallbackSurpriseTests
{
    private const string BindingId = "test.replacement.cancellationcallback";
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task InstallAsync_replaces_kernel_when_incumbent_execution_cancellation_callback_throws()
    {
        var binding = new ThrowingCancellationCallbackBinding();
        using var server = PluginServer.Create(
            configureHost: builder => builder.AddBinding(binding.Descriptor()),
            defaultPolicy: CreatePolicy());
        var incumbent = await server.InstallAsync(CreatePackage());
        using var allowRevocationToFinish = new ManualResetEventSlim(initialState: false);
        var revocationCallbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var revocationBarrier = incumbent.RevocationToken.Register(
            () =>
            {
                revocationCallbackStarted.TrySetResult();
                allowRevocationToFinish.Wait(TimeSpan.FromSeconds(5));
            });
        using var throwingRevocationCallback = incumbent.RevocationToken.Register(
            static () => throw new InvalidOperationException("The revocation callback failed."));
        var execution = incumbent.ShouldHandleAsync(EventAdapter.Instance, new ReplacementEvent()).AsTask();
        await binding.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var replacementTask = Task.Run(async () => await server.InstallAsync(CreatePackage()));
        await revocationCallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await binding.ExecutionObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        allowRevocationToFinish.Set();
        var replacement = await replacementTask;

        Assert.True(incumbent.IsRevoked);
        Assert.Same(replacement, server.Kernels.Get("replacement-cancellation-callback"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await execution);
    }

    private static BindingDescriptor Descriptor(ThrowingCancellationCallbackBinding binding)
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Bool,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, cancellationToken) => binding.InvokeAsync(cancellationToken),
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"))
        {
            IsAsync = true
        };

    private static PluginPackage CreatePackage()
    {
        var module = new SandboxModule(
            "replacement-cancellation-callback",
            SemVersion.One,
            SemVersion.One,
            [],
            [ShouldHandle(), Handle()],
            new Dictionary<string, string>
            {
                ["pluginId"] = "replacement-cancellation-callback",
                ["kernel"] = "ReplacementKernel"
            });
        var manifest = new PluginManifest(
            "replacement-cancellation-callback",
            "IEventKernel<ReplacementEvent>",
            ExecutionMode.Interpreted,
            ["Cpu", "Concurrency"],
            [],
            [new HookSubscriptionManifest(nameof(ReplacementEvent), "ReplacementKernel")])
        {
            RequiredCapabilities = [RuntimeCapabilityIds.Async]
        };

        return PluginPackage.Create(manifest, module, new KernelEntrypoints("ShouldHandle", "Handle"));
    }

    private static SandboxFunction ShouldHandle()
        => new(
            "ShouldHandle",
            true,
            [],
            SandboxType.Bool,
            [new ReturnStatement(new CallExpression(BindingId, [], null, Span), Span)]);

    private static SandboxFunction Handle()
        => new(
            "Handle",
            true,
            [],
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);

    private static SandboxPolicy CreatePolicy()
        => SandboxPolicyBuilder.Create()
            .Grant(RuntimeCapabilityIds.Async, new { }, SandboxEffect.Concurrency)
            .WithFuel(10_000)
            .WithMaxHostCalls(100)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .Build();

    private sealed class ThrowingCancellationCallbackBinding
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ExecutionObservedCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BindingDescriptor Descriptor() => KernelReplacementCancellationCallbackSurpriseTests.Descriptor(this);

        public async ValueTask<SandboxValue> InvokeAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(
                static () => throw new InvalidOperationException("The execution callback failed."));
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return SandboxValue.FromBool(true);
            }
            finally
            {
                ExecutionObservedCancellation.TrySetResult();
            }
        }
    }

    private sealed record ReplacementEvent;

    private sealed class EventAdapter : IPluginEventAdapter<ReplacementEvent>
    {
        public static EventAdapter Instance { get; } = new();
        public string EventName => nameof(ReplacementEvent);
        public IReadOnlyList<Parameter> Parameters => [];
        public IReadOnlyList<SandboxValue> ToSandboxValues(ReplacementEvent e) => [];
    }
}
