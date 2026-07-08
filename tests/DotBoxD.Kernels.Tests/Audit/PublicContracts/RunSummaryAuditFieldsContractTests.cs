using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Audit.PublicContracts;

public sealed class RunSummaryAuditFieldsContractTests
{
    [Fact]
    public void Create_rejects_null_plan()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => RunSummaryAuditFields.Create(
            null!,
            new ResourceMeter(new ResourceLimits(MaxFuel: 1_000)),
            ExecutionMode.Interpreted,
            "None"));

        Assert.Equal("plan", ex.ParamName);
    }

    [Fact]
    public async Task Create_rejects_null_budget()
    {
        var plan = await ValidPlanAsync();

        var ex = Assert.Throws<ArgumentNullException>(() => RunSummaryAuditFields.Create(
            plan,
            null!,
            ExecutionMode.Interpreted,
            "None"));

        Assert.Equal("budget", ex.ParamName);
    }

    [Fact]
    public async Task Create_rejects_undefined_mode()
    {
        var plan = await ValidPlanAsync();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => RunSummaryAuditFields.Create(
            plan,
            new ResourceMeter(plan.Budget),
            (ExecutionMode)123,
            "None"));

        Assert.Equal("mode", ex.ParamName);
    }

    [Fact]
    public async Task Create_rejects_null_cache_status()
    {
        var plan = await ValidPlanAsync();

        var ex = Assert.Throws<ArgumentNullException>(() => RunSummaryAuditFields.Create(
            plan,
            new ResourceMeter(plan.Budget),
            ExecutionMode.Interpreted,
            null!));

        Assert.Equal("cacheStatus", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_blank_cache_status(string cacheStatus)
    {
        var plan = await ValidPlanAsync();

        var ex = Assert.Throws<ArgumentException>(() => RunSummaryAuditFields.Create(
            plan,
            new ResourceMeter(plan.Budget),
            ExecutionMode.Interpreted,
            cacheStatus));

        Assert.Equal("cacheStatus", ex.ParamName);
    }

    private static async Task<ExecutionPlan> ValidPlanAsync()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithPolicyId("summary-policy")
            .WithFuel(1_000)
            .Build());
    }
}
