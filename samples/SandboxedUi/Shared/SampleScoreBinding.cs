using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace Examples.SandboxedUi.Shared;

internal static class SampleScoreBinding
{
    public static BindingDescriptor Create() => new("ui.game.score", SemVersion.One, [], SandboxType.I32,
        SandboxEffect.Cpu | SandboxEffect.HostStateRead, "game.score.read", BindingCostModel.Fixed(1), AuditLevel.PerResource,
        BindingSafety.ReadOnlyExternal, InvokeScore,

        CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"), GrantValidator: static (_, _) => { });

    private static ValueTask<SandboxValue> InvokeScore(SandboxContext context, IReadOnlyList<SandboxValue> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow;
        context.Audit.Write(new SandboxAuditEvent(context.RunId, "BindingCall", started, true,
            BindingId: "ui.game.score", CapabilityId: "game.score.read", Effect: SandboxEffect.HostStateRead,
            ResourceId: "score", Fields: context.BindingAuditFields("ui-score", started)));
        return ValueTask.FromResult(SandboxValue.FromInt32(42));
    }

}
