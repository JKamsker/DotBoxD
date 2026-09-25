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
    public async Task Subscription_racing_server_disposal_is_rejected()
    {
        using var server = CreateServer();
        var translation = new BlockingQueryValue();
        var query = server.Subscriptions.Query<RetainedQueryEvent>()
            .Where(@event => @event.Value == translation.Value);
        var subscription = Task.Run(async () =>
            await query.SubscribeAsync((_, _) => ValueTask.CompletedTask));
        await translation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        server.Dispose();
        translation.AllowCompletion.Set();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await subscription);
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

    private sealed class BlockingQueryValue
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim AllowCompletion { get; } = new(initialState: false);

        public int Value
        {
            get
            {
                Started.TrySetResult();
                AllowCompletion.Wait(TimeSpan.FromSeconds(5));
                return 0;
            }
        }
    }

    private sealed class RetainedQueryEventAdapter : IPluginEventAdapter<RetainedQueryEvent>
    {
        public static RetainedQueryEventAdapter Instance { get; } = new();

        public string EventName => nameof(RetainedQueryEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(RetainedQueryEvent @event) => [];
    }
}
