namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Cancellation;

public sealed class HookPipelineFalseFilterCancellationTests
{
    private sealed record Ping(string Target, int Value);

    [Fact]
    public async Task Synchronous_false_filter_preserves_caller_cancellation()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        using var cancellation = new CancellationTokenSource();
        var filterInvoked = false;
        var handlerInvoked = false;

        server.Hooks.On<Ping>()
            .Where((_, _) =>
            {
                filterInvoked = true;
                cancellation.Cancel();
                return false;
            })
            .RunLocal((_, _) => handlerInvoked = true);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.Hooks.PublishAsync(new Ping("monster-1", 21), cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.True(filterInvoked);
        Assert.False(handlerInvoked);
    }
}
