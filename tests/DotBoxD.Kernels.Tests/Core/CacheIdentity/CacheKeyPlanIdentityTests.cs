using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class CacheKeyPlanIdentityTests
{
    [Fact]
    public async Task Cache_key_distinguishes_adjacent_plan_identity_fields()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var template = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var policy = VerificationPolicy.BoxedValueDefaults();
        var first = WithPlanIdentity(template, bindingManifestHash: "a|b", policyHash: "c");
        var second = WithPlanIdentity(template, bindingManifestHash: "a", policyHash: "b|c");

        Assert.NotEqual(first.BindingManifestHash, second.BindingManifestHash);
        Assert.NotEqual(first.PolicyHash, second.PolicyHash);
        Assert.NotEqual(
            CacheKeyBuilder.Build(first, "main", policy, optimize: false),
            CacheKeyBuilder.Build(second, "main", policy, optimize: false));
    }

    private static ExecutionPlan WithPlanIdentity(
        ExecutionPlan template,
        string bindingManifestHash,
        string policyHash)
        => new(
            template.ModuleHash,
            template.PlanHash,
            template.PlanSeal,
            policyHash,
            bindingManifestHash,
            template.Module,
            template.Policy,
            template.Bindings,
            template.Budget,
            template.FunctionAnalysis,
            template.BindingReferences);
}
