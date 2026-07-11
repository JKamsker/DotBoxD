using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginSessionInstallAndWireCancellationTests
{
    [Fact]
    public async Task Install_and_wire_rolls_back_when_wire_cancels_caller_token()
    {
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var session = server.CreateSession();
        using var cts = new CancellationTokenSource();
        InstalledKernel? staged = null;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await session.InstallAndWireAsync(
                FireDamagePluginPackage.Create(),
                candidate =>
                {
                    staged = candidate;
                    server.WireHook(candidate);
                    cts.Cancel();
                },
                cancellationToken: cts.Token).AsTask());

        Assert.NotNull(staged);
        Assert.True(staged!.IsRevoked);
        Assert.False(server.Kernels.TryGet("fire-damage", out _));
        Assert.False(session.Owns("fire-damage"));
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
