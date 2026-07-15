using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginSessionPromotionDisposedOwnerTests
{
    [Fact]
    public async Task Promote_rejects_staged_kernel_after_owner_server_disposal()
    {
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        var staged = await session.InstallStagedAsync(FireDamagePluginPackage.Create());

        server.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Promote(staged));
        Assert.True((bool)staged.IsRevoked);
        Assert.False(server.Kernels.TryGet(staged.InstallId, out _));
        Assert.False(server.Kernels.TryGet(staged.Manifest.PluginId, out _));
    }

    [Fact]
    public async Task Install_and_wire_rejects_when_wire_callback_disposes_owner_server()
    {
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        InstalledKernel? staged = null;
        InstalledKernel? returned = null;

        var exception = await Record.ExceptionAsync(async () =>
        {
            returned = await session.InstallAndWireAsync(
                FireDamagePluginPackage.Create(),
                candidate =>
                {
                    staged = candidate;
                    server.Dispose();
                }).AsTask();
        });

        Assert.IsType<ObjectDisposedException>(exception);
        Assert.Null(returned);
        Assert.NotNull(staged);
        Assert.True((bool)staged!.IsRevoked);
        Assert.False(server.Kernels.TryGet(staged.InstallId, out _));
        Assert.False(server.Kernels.TryGet(staged.Manifest.PluginId, out _));
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
