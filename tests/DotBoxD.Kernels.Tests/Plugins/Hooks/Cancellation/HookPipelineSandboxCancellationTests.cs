using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Cancellation;

public sealed class HookPipelineSandboxCancellationTests
{
    [Fact]
    public async Task Sandbox_domain_caller_cancellation_stops_publish_before_later_handlers()
    {
        var messages = new BlockingPluginMessageSink();
        using var server = PluginAddendumTestPolicies.CreateServer(messages);
        using var cancellation = new CancellationTokenSource();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var laterHandlerInvoked = false;

        server.Hooks.On<DamageEvent>()
            .Use(kernel)
            .RunLocal((_, _) => laterHandlerInvoked = true);

        var publish = server.Hooks.PublishAsync(
            new DamageEvent("fire", 120, "player-1"),
            cancellation.Token).AsTask();
        await messages.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await publish.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(laterHandlerInvoked);
    }
}
