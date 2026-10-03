using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Inspection;
using DotBoxD.Plugins.Replay;

namespace DotBoxD.Kernels.Tests.Plugins.Inspection;

public sealed class PluginInspectionTests
{
    [Fact]
    public async Task Required_capability_explanation_matches_policy_and_audited_binding_replays()
    {
        var binding = SafeTimeBindings.NowUnixMillis;
        var registry = new BindingRegistry([binding]);
        var module = new SandboxModule("clock-test", SemVersion.One, SemVersion.One,
            [new CapabilityRequest("time.now", "Display time")],
            [new SandboxFunction("main", true, [], SandboxType.I64,
                [new ReturnStatement(new CallExpression(binding.Id, [], null, new SourceSpan(3, 4)), new SourceSpan(3, 1))])],
            new Dictionary<string, string>());
        var denied = SandboxPolicyBuilder.Create().Build();
        var explanation = PluginInspection.Explain(module, registry, denied);
        Assert.False(explanation.Valid);
        Assert.False(Assert.Single(explanation.Capabilities).Granted);
        Assert.Contains(explanation.Operations, operation => operation.Operation == $"call {binding.Id}" && operation.Span.Line == 3);
        var allowed = denied with
        {
            AllowedEffects = denied.AllowedEffects | SandboxEffect.Time,
            Grants = [new CapabilityGrant("time.now", new Dictionary<string, string>())],
            ResourceLimits = new ResourceLimits(MaxWallTime: TimeSpan.FromSeconds(5))
        };
        Assert.True(Assert.Single(PluginInspection.Explain(module, registry, allowed).Capabilities).Granted);
        var dated = allowed with
        {
            Deterministic = false,
            LogicalNow = DateTimeOffset.UnixEpoch,
            Grants = [new CapabilityGrant("time.now", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch.AddDays(1))]
        };
        Assert.False(Assert.Single(PluginInspection.Explain(module, registry, dated).Capabilities).Granted);
        Assert.True(Assert.Single(PluginInspection.Explain(module, registry, dated with { Deterministic = true }).Capabilities).Granted);
        var trace = await ExecutionRecording.CaptureAsync(module, allowed, [binding], "main", SandboxValue.Unit);
        Assert.Null(trace.ErrorCode);
        Assert.NotEmpty(Assert.Single(trace.Calls).AuditEvents);
        var replay = await ExecutionReplay.RunAsync(ExecutionTrace.Deserialize(trace.Serialize()));
        Assert.True(replay.Matches, replay.Difference);
    }

    [Fact]
    public async Task Auto_explanation_uses_the_public_selector_and_respects_dynamic_code_availability()
    {
        var module = new SandboxModule("inspect-pure", SemVersion.One, SemVersion.One, [],
            [new SandboxFunction("main", true, [], SandboxType.I32,
                [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(1), new SourceSpan(1, 1)), new SourceSpan(1, 1))])],
            new Dictionary<string, string>());
        using var host = SandboxHost.Create();
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        var options = new SandboxExecutionOptions { Mode = ExecutionMode.Auto, AutoCompileThreshold = 2 };
        Assert.Equal(ExecutionMode.Interpreted, PluginInspection.ExplainExecution(plan, "main", options).SelectedMode);
        var unavailable = PluginInspection.ExplainExecution(plan, "main", options, runCount: 100, compilerConfigured: false);
        Assert.False(unavailable.CompiledCandidate);
        Assert.Contains("unavailable", unavailable.Reason, StringComparison.Ordinal);
        Assert.Equal(ExecutionMode.Compiled, PluginInspection.ExplainExecution(plan, "main", options, runCount: 2).SelectedMode);
    }
}
