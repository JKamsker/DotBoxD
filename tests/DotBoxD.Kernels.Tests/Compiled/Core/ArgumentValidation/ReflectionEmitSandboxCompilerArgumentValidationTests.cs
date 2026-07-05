using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Core.ArgumentValidation;

public sealed class ReflectionEmitSandboxCompilerArgumentValidationTests
{
    [Fact]
    public void Constructor_rejects_null_verifier_with_public_parameter_name()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new ReflectionEmitSandboxCompiler(null!));

        Assert.Equal("verifier", ex.ParamName);
    }

    [Fact]
    public void CompileOptions_rejects_null_entrypoint_with_public_parameter_name()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new CompileOptions(null!));

        Assert.Equal("Entrypoint", ex.ParamName);
    }

    [Fact]
    public void CompileOptions_rejects_null_entrypoint_with_init_parameter_name()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new CompileOptions("main") { Entrypoint = null! });

        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public void CompileOptions_rejects_null_entrypoint_with_expression_parameter_name()
    {
        var options = new CompileOptions("main");

        var ex = Assert.Throws<ArgumentNullException>(
            () => options with { Entrypoint = null! });

        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public async Task CompileAsync_rejects_null_plan_with_public_parameter_name()
    {
        var compiler = CreateCompiler();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await compiler.CompileAsync(
                    null!,
                    new CompileOptions("main"),
                    CancellationToken.None)
                .AsTask());

        Assert.Equal("plan", ex.ParamName);
    }

    [Fact]
    public async Task CompileAsync_rejects_null_options_with_public_parameter_name()
    {
        var plan = await PreparePlanAsync();
        var compiler = CreateCompiler();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await compiler.CompileAsync(
                    plan,
                    null!,
                    CancellationToken.None)
                .AsTask());

        Assert.Equal("options", ex.ParamName);
    }

    private static ReflectionEmitSandboxCompiler CreateCompiler()
        => new(new GeneratedAssemblyVerifier());

    private static async Task<ExecutionPlan> PreparePlanAsync()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }
}
