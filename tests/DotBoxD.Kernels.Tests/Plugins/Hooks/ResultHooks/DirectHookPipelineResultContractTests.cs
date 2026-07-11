using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class DirectHookPipelineResultContractTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task UseResult_rejects_result_subscription_when_event_has_no_hook_result_contract()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(ResultHookPackage());
        var pipeline = server.Hooks.On<NoHookEvent>(new NoHookEventAdapter());

        var exception = Record.Exception(() => pipeline.UseResult(kernel, typeof(DamageResult)));

        Assert.NotNull(exception);
        Assert.True(
            exception is InvalidOperationException or SandboxValidationException,
            $"Expected InvalidOperationException or SandboxValidationException, got {exception.GetType()}.");
        Assert.Contains("[Hook]", exception.Message, StringComparison.Ordinal);
        Assert.Contains("result", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PluginPackage ResultHookPackage()
        => PluginPackage.Create(
            new PluginManifest(
                "direct-result-no-hook-contract",
                $"IEventKernel<{typeof(NoHookEvent).FullName}>",
                ExecutionMode.Interpreted,
                ["Cpu", "Alloc"],
                [],
                [
                    new HookSubscriptionManifest(typeof(NoHookEvent).FullName!, "ResultKernel")
                    {
                        ResultType = typeof(DamageResult).FullName
                    }
                ]),
            new SandboxModule(
                "direct-result-no-hook-contract",
                SemVersion.One,
                SemVersion.One,
                [],
                [ShouldHandle(), Handle()],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["kernel"] = "ResultKernel",
                    ["pluginId"] = "direct-result-no-hook-contract"
                }),
            new KernelEntrypoints("ShouldHandle", "Handle"));

    private static SandboxFunction ShouldHandle()
        => new(
            "ShouldHandle",
            IsEntrypoint: true,
            [],
            SandboxType.Bool,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromBool(true), Span), Span)]);

    private static SandboxFunction Handle()
        => new(
            "Handle",
            IsEntrypoint: true,
            [],
            SandboxType.Record([SandboxType.Bool, SandboxType.String, SandboxType.I32]),
            [
                new ReturnStatement(
                    new LiteralExpression(
                        SandboxValue.FromRecord([
                            SandboxValue.FromBool(true),
                            SandboxValue.FromString("accepted"),
                            SandboxValue.FromInt32(10)
                        ]),
                        Span),
                    Span)
            ]);

    private sealed record NoHookEvent;

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;

    private sealed class NoHookEventAdapter : IPluginEventAdapter<NoHookEvent>
    {
        public string EventName => typeof(NoHookEvent).FullName!;

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(NoHookEvent e) => [];
    }
}
