using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GameServerPolicyTests
{
    [Fact]
    public void Policy_does_not_grant_undeclared_monster_write_capabilities()
    {
        var policy = InvokePolicy("ForKernel", "host.message.write");

        Assert.False(policy.GrantsCapability("game.world.monster.write.kill"));
        Assert.DoesNotContain(
            policy.Grants,
            grant => grant.Id.StartsWith("game.world.monster.write.", StringComparison.Ordinal));
    }

    [Fact]
    public void Policy_grants_declared_monster_write_capabilities()
    {
        var policy = InvokePolicy("ForKernel", "game.world.monster.write.kill");

        Assert.True(policy.GrantsCapability("game.world.monster.write.kill"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Demo_policies_tolerate_startup_latency_without_changing_library_limits(bool kernelPolicy)
    {
        var policy = kernelPolicy
            ? InvokePolicy("ForKernel", "game.world.monster.write.kill")
            : InvokePolicy("Create");
        var meter = new ResourceMeter(policy.ResourceLimits);
        var libraryDefault = new ResourceMeter(new ResourceLimits());

        // Cold execution and scheduling pauses can exceed the library's 100 ms default.
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        meter.CheckDeadline();
        Assert.InRange(meter.RemainingWallTime(), TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Assert.Equal(SandboxErrorCode.Timeout,
            Assert.Throws<SandboxRuntimeException>(libraryDefault.CheckDeadline).Error.Code);
        Assert.Equal(100_000, policy.ResourceLimits.MaxFuel);
        Assert.Equal(1_000, policy.ResourceLimits.MaxHostCalls);
        Assert.Equal(SandboxErrorCode.QuotaExceeded,
            Assert.Throws<SandboxRuntimeException>(() => meter.ChargeFuel(100_001)).Error.Code);
        for (var index = 0; index < 1_000; index++)
        {
            meter.ChargeHostCall("demo-binding");
        }

        Assert.Equal(SandboxErrorCode.QuotaExceeded,
            Assert.Throws<SandboxRuntimeException>(() => meter.ChargeHostCall("demo-binding")).Error.Code);
    }

    private static SandboxPolicy InvokePolicy(string methodName, params string[] requiredCapabilities)
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var serverPolicy = gameServer.GetType("DotBoxD.Kernels.Game.Server.ServerPolicy", throwOnError: true)!;
        var result = serverPolicy
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, methodName == "Create" ? [] : [requiredCapabilities]);
        return (SandboxPolicy)result!;
    }

    private static string GameServerAssemblyPath()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configuration = output.Parent!.Name;
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "GameServer",
            "Examples.GameServer.Server",
            "bin",
            configuration,
            "net10.0",
            "Examples.GameServer.Server.dll"));
    }
}
