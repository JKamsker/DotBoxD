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
    {
        var plan = await CreatePlanAsync();
        var policy = VerificationPolicy.BoxedValueDefaults();
        Action action = parameterName switch
        {
            "plan" => () => CacheKeyBuilder.Build(null!, "main", policy, optimize: false),
            "entrypoint" => () => CacheKeyBuilder.Build(plan, null!, policy, optimize: false),
            "policy" => () => CacheKeyBuilder.Build(plan, "main", null!, optimize: false),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterName), parameterName, null)
        };

        AssertArgumentNull(parameterName, action);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("entrypoint")]
    [InlineData("policy")]
    public async Task BuildManifestIdentity_rejects_null_public_inputs(string parameterName)
    {
        var plan = await CreatePlanAsync();
        var policy = VerificationPolicy.BoxedValueDefaults();
        Action action = parameterName switch
        {
            "plan" => () => CacheKeyBuilder.BuildManifestIdentity(null!, "main", policy, optimize: false),
            "entrypoint" => () => CacheKeyBuilder.BuildManifestIdentity(plan, null!, policy, optimize: false),
            "policy" => () => CacheKeyBuilder.BuildManifestIdentity(plan, "main", null!, optimize: false),
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
