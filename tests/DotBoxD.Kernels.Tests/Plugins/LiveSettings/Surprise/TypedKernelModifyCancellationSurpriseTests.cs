using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class TypedKernelModifyCancellationSurpriseTests
{
    [Fact]
    public async Task Class_typed_modify_observes_token_canceled_by_modifier_before_committing_settings()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await kernel.ModifyAsync(
                state =>
                {
                    state.MinDamage = 250;
                    cts.Cancel();
                },
                atomic: true,
                cancellationToken: cts.Token).AsTask());

        Assert.Equal(100, kernel.Value.MinDamage);
        Assert.Equal(100, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task Interface_typed_modify_observes_token_canceled_by_modifier_before_committing_settings()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var installed = await server.InstallAsync(FireDamagePluginPackage.Create());
        var settings = installed.As<IFireDamageSettings>();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await settings.ModifyAsync(
                state =>
                {
                    state.MinDamage = 250;
                    cts.Cancel();
                },
                atomic: true,
                cancellationToken: cts.Token).AsTask());

        Assert.Equal(100, settings.Value.MinDamage);
        Assert.Equal(100, installed.Value.Get<int>("MinDamage"));
    }
}
