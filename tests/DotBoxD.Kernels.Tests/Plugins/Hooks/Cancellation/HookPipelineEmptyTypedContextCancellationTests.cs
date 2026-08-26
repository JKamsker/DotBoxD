namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Cancellation;

public sealed class HookPipelineEmptyTypedContextCancellationTests
{
    private sealed record Ping;

    private sealed record CustomContext(HookContext Raw);

    [Fact]
    public async Task Empty_typed_pipeline_preserves_caller_cancellation_from_context_factory()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        using var cancellation = new CancellationTokenSource();
        var factoryInvoked = false;

        server.Hooks.On<Ping, CustomContext>(raw =>
        {
            factoryInvoked = true;
            cancellation.Cancel();
            return new CustomContext(raw);
        });

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.Hooks.PublishAsync(new Ping(), cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.True(factoryInvoked);
    }

    [Fact]
    public async Task Empty_typed_pipeline_without_cancellation_completes()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();

        server.Hooks.On<Ping, CustomContext>(raw => new CustomContext(raw));

        await server.Hooks.PublishAsync(new Ping());
    }
}
