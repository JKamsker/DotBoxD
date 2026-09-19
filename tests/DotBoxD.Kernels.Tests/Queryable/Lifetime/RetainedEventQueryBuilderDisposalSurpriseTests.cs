using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Queryable;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class RetainedEventQueryBuilderDisposalSurpriseTests
{
    [Fact]
    public async Task Retained_identity_query_rejects_subscription_after_server_disposal()
    {
        using var server = CreateServer();
        var query = server.Subscriptions.Query<RetainedQueryEvent>();

        server.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await query.SubscribeAsync((_, _) => ValueTask.CompletedTask));
    }

    [Fact]
    public async Task Retained_projected_query_rejects_subscription_after_server_disposal()
    {
        using var server = CreateServer();
        var query = server.Subscriptions.Query<RetainedQueryEvent>()
            .Select(@event => @event.Value);

        server.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await query.SubscribeAsync((_, _) => ValueTask.CompletedTask));
    }

    [Fact]
    public async Task Standalone_host_allows_identity_and_projected_subscriptions()
    {
        var host = new EventQueryHost();

        await host.Query<RetainedQueryEvent>()
            .SubscribeAsync((_, _) => ValueTask.CompletedTask);
        await host.Query<RetainedQueryEvent>()
            .Select(@event => @event.Value)
            .SubscribeAsync((_, _) => ValueTask.CompletedTask);

        Assert.True(host.HasSubscriptions<RetainedQueryEvent>());
    }

    private static PluginServer CreateServer()
    {
        var server = PluginServer.Create();
        server.RegisterEventAdapter(RetainedQueryEventAdapter.Instance);
        return server;
    }

    private sealed record RetainedQueryEvent(int Value);

    private sealed class RetainedQueryEventAdapter : IPluginEventAdapter<RetainedQueryEvent>
    {
        public static RetainedQueryEventAdapter Instance { get; } = new();

        public string EventName => nameof(RetainedQueryEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(RetainedQueryEvent @event) => [];
    }
}
