using CsCheck;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Fuzz;

public sealed class EffectSoundnessPropertyTests
{
    private static readonly EffectCase Time = new(
        "time.nowUnixMillis",
        SandboxEffect.Time,
        SandboxPolicyBuilder.Create().GrantTimeNow().Deterministic(DateTimeOffset.UnixEpoch, 1).Build(),
        """
        {
          "id":"effect-time","version":"1.0.0","capabilityRequests":[{"id":"time.now"}],
          "functions":[{"id":"main","visibility":"entrypoint","parameters":[],"returnType":"I64",
          "body":[{"op":"return","value":{"call":"time.nowUnixMillis","args":[]}}]}]
        }
        """);

    private static readonly EffectCase Random = new(
        "random.nextI32",
        SandboxEffect.Random,
        SandboxPolicyBuilder.Create().GrantRandom().Deterministic(DateTimeOffset.UnixEpoch, 1).Build(),
        """
        {
          "id":"effect-random","version":"1.0.0","capabilityRequests":[{"id":"random"}],
          "functions":[{"id":"main","visibility":"entrypoint","parameters":[],"returnType":"I32",
          "body":[{"op":"return","value":{"call":"random.nextI32","args":[{"i32":0},{"i32":10}]}}]}]
        }
        """);

    [Fact]
    public void Declared_effects_cover_observed_capability_effects()
        => Gen.OneOf(Gen.Const(Time), Gen.Const(Random)).Sample(
            testCase => VerifyAsync(testCase).GetAwaiter().GetResult(),
            seed: "0N0XIzNsQ0O2",
            iter: 40,
            threads: 1);

    private static async Task VerifyAsync(EffectCase testCase)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(testCase.Json);
        var plan = await host.PrepareAsync(module, testCase.Policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Contains(result.AuditEvents, item => item.BindingId == testCase.BindingId && item.Success);
        var declared = plan.FunctionAnalysis["main"].Effects;
        Assert.Equal(testCase.ObservedEffect, declared & testCase.ObservedEffect);
    }

    private sealed record EffectCase(
        string BindingId,
        SandboxEffect ObservedEffect,
        SandboxPolicy Policy,
        string Json);
}
