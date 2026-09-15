using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class DirectHookFilterDisposalSurpriseTests
{
    [Fact]
    public async Task Successful_filter_disposal_stops_later_direct_hook_callbacks()
    {
        PluginServer? server = PluginServer.Create();
        var laterFilterInvoked = false;
        var handlerInvoked = false;

        server.Hooks.On<Signal>()
            .Where(_ =>
            {
                server.Dispose();
                return true;
            })
            .Where(_ =>
            {
                laterFilterInvoked = true;
                return true;
            })
            .RunLocal(_ => handlerInvoked = true);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => server.Hooks.PublishAsync(new Signal()).AsTask());

        Assert.False(laterFilterInvoked);
        Assert.False(handlerInvoked);
    }

    private sealed record Signal;
}
