using System.Security.Cryptography;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Execution;

internal sealed class PreparedPlanIdentityFixture(ExecutionPlan plan, byte[] signingKey)
{
    internal ExecutionPlan Plan => plan;

    internal static async Task<PreparedPlanIdentityFixture> CreateAsync()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000).WithWallTime(TimeSpan.FromSeconds(10)).Build();
        var bindings = new BindingRegistryBuilder().Build();
        var validation = new ModuleValidator().Validate(module, bindings, policy);
        Assert.True(validation.Succeeded);
        var key = RandomNumberGenerator.GetBytes(32);
        return new PreparedPlanIdentityFixture(
            ExecutionPlanBuilder.Build(module, policy, bindings, validation.Functions, validation.BindingReferences, key), key);
    }

    internal void Validate(ExecutionPlan candidate, bool warmCache)
    {
        var cache = new PreparedPlanIntegrityCache();
        if (warmCache)
        {
            cache.Register(plan);
        }
        ExecutionPlanGuard.EnsurePrepared(candidate, plan.Bindings, signingKey, cache);
    }

    internal ExecutionPlan Copy(string? changedField = null, bool copyValues = false)
    {
        string Metadata(string value, string field) => changedField == field
            ? "inconsistent-test-metadata"
            : copyValues ? new string(value.ToCharArray()) : value;

        return new ExecutionPlan(
            Metadata(plan.ModuleHash, "ModuleHash"),
            Metadata(plan.PlanHash, "PlanHash"),
            plan.PlanSeal,
            Metadata(plan.PolicyHash, "PolicyHash"),
            Metadata(plan.BindingManifestHash, "BindingManifestHash"),
            plan.Module,
            plan.Policy,
            plan.Bindings,
            changedField == "Budget" ? plan.Budget with { MaxFuel = 999 } : copyValues ? plan.Budget with { } : plan.Budget,
            copyValues
                ? plan.FunctionAnalysis.ToDictionary(pair => pair.Key, pair => pair.Value with { }, StringComparer.Ordinal)
                : plan.FunctionAnalysis,
            plan.BindingReferences);
    }
}
