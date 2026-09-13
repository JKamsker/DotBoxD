using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Queryable;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class IntegratedEventQueryDisposalSurpriseTests
{
    [Fact]
    public async Task Projection_disposal_stops_the_integrated_query_before_its_handler()
    {
        using var server = PluginServer.Create();
        server.RegisterEventAdapter(QueryEventAdapter.Instance);

        var projectionDisposedServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = await server.Subscriptions.Query<DisposingProjectionEvent>()
            .Select(@event => @event.ProjectedValue)
            .SubscribeAsync((_, _) =>
            {
                handlerInvoked.TrySetResult();
                return ValueTask.CompletedTask;
            });

        server.Subscriptions.Publish(new DisposingProjectionEvent(server, projectionDisposedServer));

        await projectionDisposedServer.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completed = await Task.WhenAny(handlerInvoked.Task, Task.Delay(TimeSpan.FromMilliseconds(200)));

        Assert.NotSame(handlerInvoked.Task, completed);
        Assert.Equal(0, handle.Dispatches);
    }

    private sealed class DisposingProjectionEvent(
        PluginServer server,
        TaskCompletionSource projectionDisposedServer)
    {
        public int ProjectedValue
        {
            get
            {
                server.Dispose();
                projectionDisposedServer.TrySetResult();
                return 42;
            }
        }
    }

    private sealed class QueryEventAdapter : IPluginEventAdapter<DisposingProjectionEvent>
    {
        public static QueryEventAdapter Instance { get; } = new();

        public string EventName => nameof(DisposingProjectionEvent);

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DisposingProjectionEvent @event) => [];
    }
}
