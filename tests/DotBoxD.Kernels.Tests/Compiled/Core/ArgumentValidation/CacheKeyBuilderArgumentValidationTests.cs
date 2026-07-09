using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;

namespace DotBoxD.Kernels.Tests.Compiled.Core.ArgumentValidation;

public sealed class CacheKeyBuilderArgumentValidationTests
{
    [Theory]
    [InlineData("plan")]
    [InlineData("entrypoint")]
    [InlineData("policy")]
    public async Task Build_rejects_null_public_inputs(string parameterName)
        => await AssertBuilderRejectsNullAsync(
            parameterName,
            (plan, entrypoint, policy) => () => CacheKeyBuilder.Build(plan!, entrypoint!, policy!, optimize: false));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Build_rejects_blank_entrypoint(string entrypoint)
    {
        var plan = await CreatePlanAsync();
        var policy = VerificationPolicy.BoxedValueDefaults();

        var ex = Assert.Throws<ArgumentException>(
            () => CacheKeyBuilder.Build(plan, entrypoint, policy, optimize: false));

        Assert.Equal("entrypoint", ex.ParamName);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("entrypoint")]
    [InlineData("policy")]
    public async Task BuildManifestIdentity_rejects_null_public_inputs(string parameterName)
        => await AssertBuilderRejectsNullAsync(
            parameterName,
            (plan, entrypoint, policy) => () => CacheKeyBuilder.BuildManifestIdentity(plan!, entrypoint!, policy!, optimize: false));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildManifestIdentity_rejects_blank_entrypoint(string entrypoint)
    {
        var plan = await CreatePlanAsync();
        var policy = VerificationPolicy.BoxedValueDefaults();

        var ex = Assert.Throws<ArgumentException>(
            () => CacheKeyBuilder.BuildManifestIdentity(plan, entrypoint, policy, optimize: false));

        Assert.Equal("entrypoint", ex.ParamName);
    }

    private static async Task AssertBuilderRejectsNullAsync(
        string parameterName,
        Func<ExecutionPlan?, string?, VerificationPolicy?, Action> actionFactory)
    {
        var plan = await CreatePlanAsync();
        var policy = VerificationPolicy.BoxedValueDefaults();
        var action = parameterName switch
        {
            "plan" => actionFactory(null, "main", policy),
            "entrypoint" => actionFactory(plan, null, policy),
            "policy" => actionFactory(plan, "main", null),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterName), parameterName, null)
        };

        AssertArgumentNull(parameterName, action);
    }

    private static async Task<ExecutionPlan> CreatePlanAsync()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static void AssertArgumentNull(string paramName, Action action)
    {
        var exception = Assert.Throws<ArgumentNullException>(action);

        Assert.Equal(paramName, exception.ParamName);
    }
}
