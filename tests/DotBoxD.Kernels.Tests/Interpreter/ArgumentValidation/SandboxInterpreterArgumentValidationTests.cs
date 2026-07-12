using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.ArgumentValidation;

public sealed class SandboxInterpreterArgumentValidationTests
{
    [Fact]
    public async Task ExecuteAsync_rejects_null_plan_with_public_parameter_name()
    {
        var interpreter = new SandboxInterpreter();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await interpreter.ExecuteAsync(
                    null!,
                    "main",
                    ValidInput(),
                    new SandboxExecutionOptions(),
                    CancellationToken.None)
                .AsTask());

        Assert.Equal("plan", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_null_options_with_public_parameter_name()
    {
        var plan = await PreparePureScorePlanAsync();
        var interpreter = new SandboxInterpreter();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await interpreter.ExecuteAsync(
                    plan,
                    "main",
                    ValidInput(),
                    null!,
                    CancellationToken.None)
                .AsTask());

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_null_entrypoint_with_public_parameter_name()
    {
        var plan = await PreparePureScorePlanAsync();
        var interpreter = new SandboxInterpreter();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await interpreter.ExecuteAsync(
                    plan,
                    null!,
                    ValidInput(),
                    new SandboxExecutionOptions(),
                    CancellationToken.None)
                .AsTask());

        Assert.Equal("entrypoint", ex.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_null_input_with_public_parameter_name()
    {
        var plan = await PreparePureScorePlanAsync();
        var interpreter = new SandboxInterpreter();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await interpreter.ExecuteAsync(
                    plan,
                    "main",
                    null!,
                    new SandboxExecutionOptions(),
                    CancellationToken.None)
                .AsTask());

        Assert.Equal("input", ex.ParamName);
    }

    private static async Task<ExecutionPlan> PreparePureScorePlanAsync()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxValue ValidInput()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
}
