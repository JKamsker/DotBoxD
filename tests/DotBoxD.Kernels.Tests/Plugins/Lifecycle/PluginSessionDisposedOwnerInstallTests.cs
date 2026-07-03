using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginSessionDisposedOwnerInstallTests
{
    [Fact]
    public async Task Session_install_and_wire_rejects_disposed_owner_before_callbacks()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        server.Dispose();
        var validateCalls = 0;
        var policyCalls = 0;
        var wireCalls = 0;

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await session.InstallAndWireAsync(
                FireDamagePluginPackage.Create(),
                _ => wireCalls++,
                _ =>
                {
                    policyCalls++;
                    return LongWallPluginPolicy();
                },
                _ => validateCalls++).AsTask());

        Assert.Equal(0, validateCalls);
        Assert.Equal(0, policyCalls);
        Assert.Equal(0, wireCalls);
    }

    [Fact]
    public async Task Session_install_and_wire_disposed_owner_cannot_be_masked_by_validate()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        server.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await session.InstallAndWireAsync(
                FireDamagePluginPackage.Create(),
                _ => { },
                validate: _ => throw new InvalidOperationException("validate should not run")).AsTask());
    }

    private static SandboxPolicy LongWallPluginPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
