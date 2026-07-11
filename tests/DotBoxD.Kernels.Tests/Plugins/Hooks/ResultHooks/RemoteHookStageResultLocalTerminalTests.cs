using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteHookStageResultLocalTerminalTests
{
    [Hook("remote.damage", typeof(DamageResult))]
    private sealed record DamageEvent(int Damage);

    private sealed record DamageContext(HookContext Raw);

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;

    [Fact]
    public void Untyped_projected_stage_rejects_local_result_terminals()
    {
        var stage = new RemoteHookRegistry(_ => ValueTask.FromResult("unused"))
            .On<DamageEvent>()
            .Select(
                e => e.Damage,
                RemoteIrTestSteps.Ir<DamageEvent, int>(LoweredPipelineStepKind.Projection));

        var exception = Assert.Throws<NotSupportedException>(() =>
            stage.UseGeneratedLocalResultChain<DamageResult>(
                ResultPackage(),
                damage => new DamageResult(true, null, damage)));

        Assert.Contains("after Select", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_projected_stage_rejects_local_result_terminals()
    {
        var stage = new RemoteHookRegistry(_ => ValueTask.FromResult("unused"))
            .On<DamageEvent, DamageContext>(ctx => new DamageContext(ctx))
            .Select(
                (e, _) => e.Damage,
                RemoteIrTestSteps.Ir<DamageEvent, DamageContext, int>(LoweredPipelineStepKind.Projection));

        var exception = Assert.Throws<NotSupportedException>(() =>
            stage.UseGeneratedLocalResultChain<DamageResult>(
                ResultPackage(),
                (damage, _) => new DamageResult(true, null, damage)));

        Assert.Contains("after Select", exception.Message, StringComparison.Ordinal);
    }

    private static PluginPackage ResultPackage()
        => PluginPackage.Create(
            new PluginManifest(
                "remote-stage-result-local",
                $"IEventKernel<{typeof(DamageEvent).FullName}>",
                ExecutionMode.Auto,
                [],
                [],
                [
                    new HookSubscriptionManifest(typeof(DamageEvent).FullName!, "ResultKernel")
                    {
                        ResultLocalTerminal = true,
                        ResultType = typeof(DamageResult).FullName
                    }
                ]),
            new SandboxModule(
                "remote-stage-result-local",
                SemVersion.One,
                SemVersion.One,
                [],
                [],
                new Dictionary<string, string>(StringComparer.Ordinal)),
            new KernelEntrypoints("ShouldHandle", "Handle"));
}
