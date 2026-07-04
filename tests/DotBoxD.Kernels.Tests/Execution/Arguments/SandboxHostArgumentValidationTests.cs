using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Execution;

public sealed class SandboxHostArgumentValidationTests
{
    [Fact]
    public async Task Prepare_rejects_null_policy_argument()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await host.PrepareAsync(module, null!));

        Assert.Equal("policy", ex.ParamName);
    }

    [Fact]
    public async Task Execute_rejects_null_entrypoint_argument()
    {
        var host = SandboxTestHost.Create();
        var plan = await PreparePureScorePlan(host);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await host.ExecuteAsync(plan, null!, ValidInput()));

        Assert.Equal("entrypoint", ex.ParamName);
    }

    [Fact]
    public async Task Execute_rejects_null_input_argument()
    {
        var host = SandboxTestHost.Create();
        var plan = await PreparePureScorePlan(host);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await host.ExecuteAsync(plan, "main", null!));

        Assert.Equal("input", ex.ParamName);
    }

    private static async ValueTask<ExecutionPlan> PreparePureScorePlan(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxValue ValidInput()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
}
