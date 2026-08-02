using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryPublishCancellationTests
{
    [Fact]
    public async Task Mid_publish_cancellation_stops_before_later_subscription_work()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var firstInvocations = 0;
        var secondInvocations = 0;

        var first = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == "player-1")
            .SubscribeAsync((_, _) =>
            {
                firstInvocations++;
                cancellation.Cancel();
                return ValueTask.CompletedTask;
            });
        var second = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == "player-1")
            .SubscribeAsync((_, _) =>
            {
                secondInvocations++;
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await host.PublishAsync(new AttackTestEvent("player-1", "monster-1", 9, 1), context));

        Assert.Equal(1, firstInvocations);
        Assert.Equal(1, first.FilterEvaluations);
        Assert.Equal(1, first.Matches);
        Assert.Equal(1, first.Dispatches);

        Assert.Equal(0, secondInvocations);
        Assert.Equal(0, second.FilterEvaluations);
        Assert.Equal(0, second.Matches);
        Assert.Equal(0, second.Dispatches);
    }

    [Fact]
    public async Task Sandbox_domain_caller_cancellation_is_not_swallowed_after_dispatch()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var invocations = 0;

        var handle = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == "player-1")
            .SubscribeAsync((_, _) =>
            {
                invocations++;
                cancellation.Cancel();
                throw new SandboxRuntimeException(
                    new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled"));
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        var exception = await Record.ExceptionAsync(
            async () => await host.PublishAsync(new AttackTestEvent("player-1", "monster-1", 9, 1), context));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(1, invocations);
        Assert.Equal(1, handle.FilterEvaluations);
        Assert.Equal(1, handle.Matches);
        Assert.Equal(0, handle.Dispatches);
    }

    [Fact]
    public async Task Ordinary_handler_fault_does_not_swallow_caller_cancellation_after_dispatch()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var invocations = 0;

        var handle = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == "player-1")
            .SubscribeAsync((_, _) =>
            {
                invocations++;
                cancellation.Cancel();
                throw new InvalidOperationException("handler failed after cancellation");
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        var exception = await Record.ExceptionAsync(
            async () => await host.PublishAsync(new AttackTestEvent("player-1", "monster-1", 9, 1), context));

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(1, invocations);
        Assert.Equal(1, handle.FilterEvaluations);
        Assert.Equal(1, handle.Matches);
        Assert.Equal(0, handle.Dispatches);
    }

    [Fact]
    public async Task Projection_cancellation_stops_before_subscription_handler_dispatch()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var handlerInvoked = false;

        var handle = await host.Query<ProjectionCancelEvent>()
            .Where(e => e.AttackerId == "player-1")
            .Select(e => new DamageProjection(e.Damage))
            .SubscribeAsync((_, _) =>
            {
                handlerInvoked = true;
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await host.PublishAsync(new ProjectionCancelEvent("player-1", cancellation), context));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(handlerInvoked);
        Assert.Equal(1, handle.FilterEvaluations);
        Assert.Equal(1, handle.Matches);
        Assert.Equal(0, handle.Dispatches);
    }

    [Fact]
    public async Task Projection_fault_does_not_swallow_caller_cancellation()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var handlerInvoked = false;

        var handle = await host.Query<ProjectionFaultCancelEvent>()
            .Where(e => e.AttackerId == "player-1")
            .Select(e => new DamageProjection(e.Damage))
            .SubscribeAsync((_, _) =>
            {
                handlerInvoked = true;
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        var exception = await Record.ExceptionAsync(
            async () => await host.PublishAsync(new ProjectionFaultCancelEvent("player-1", cancellation), context));

        var cancellationException = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, cancellationException.CancellationToken);
        Assert.False(handlerInvoked);
        Assert.Equal(1, handle.FilterEvaluations);
        Assert.Equal(1, handle.Matches);
        Assert.Equal(0, handle.Dispatches);
    }

    [Fact]
    public async Task Indexed_routing_key_cancellation_stops_even_when_key_misses()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var handlerInvoked = false;

        var handle = await host.Query<RoutingCancelEvent>()
            .Where(e => e.AttackerId == "player-1")
            .SubscribeAsync((_, _) =>
            {
                handlerInvoked = true;
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await host.PublishAsync(new RoutingCancelEvent(cancellation), context));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(handlerInvoked);
        Assert.Equal(0, handle.FilterEvaluations);
        Assert.Equal(0, handle.Matches);
        Assert.Equal(0, handle.Dispatches);
    }

    [Fact]
    public async Task Nonmatching_broad_filter_does_not_swallow_caller_cancellation()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var handlerInvoked = false;

        var handle = await host.Query<FilterCancelEvent>()
            .Where(e => e.Damage > 10)
            .SubscribeAsync((_, _) =>
            {
                handlerInvoked = true;
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        var exception = await Record.ExceptionAsync(
            async () => await host.PublishAsync(new FilterCancelEvent(cancellation), context));

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(handlerInvoked);
        Assert.Equal(1, handle.FilterEvaluations);
        Assert.Equal(0, handle.Matches);
        Assert.Equal(0, handle.Dispatches);
    }

    private sealed class ProjectionCancelEvent(string attackerId, CancellationTokenSource cancellation)
    {
        public string AttackerId { get; } = attackerId;

        public int Damage
        {
            get
            {
                cancellation.Cancel();
                return 9;
            }
        }
    }

    private sealed class ProjectionFaultCancelEvent(string attackerId, CancellationTokenSource cancellation)
    {
        public string AttackerId { get; } = attackerId;

        public int Damage
        {
            get
            {
                cancellation.Cancel();
                throw new InvalidOperationException("projection failed");
            }
        }
    }

    private sealed class RoutingCancelEvent(CancellationTokenSource cancellation)
    {
        public string AttackerId
        {
            get
            {
                cancellation.Cancel();
                return "player-2";
            }
        }
    }

    private sealed class FilterCancelEvent(CancellationTokenSource cancellation)
    {
        public int Damage
        {
            get
            {
                cancellation.Cancel();
                return 0;
            }
        }
    }

    private sealed record DamageProjection(int Damage);
}
