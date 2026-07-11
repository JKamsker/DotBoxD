using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQuerySubscriptionDocumentMutationTests
{
    private static HookContext NewContext() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    [Fact]
    public async Task Subscription_document_does_not_expose_mutable_in_filter_values()
    {
        var host = new EventQueryHost();
        var attackerIds = new[] { "allowed" };

        var handle = await host.Query<AttackTestEvent>()
            .Where(e => attackerIds.Contains(e.AttackerId))
            .SubscribeAsync((_, _) => ValueTask.CompletedTask);

        Assert.Equal(QueryFilterKind.In, handle.Document.Filter.Kind);
        Assert.False(
            handle.Document.Filter.Values is QueryValue[],
            "Subscription documents must not expose QueryFilter.In values as a mutable array.");
    }

    [Fact]
    public async Task Mutating_subscription_document_in_values_after_registration_does_not_change_dispatch()
    {
        var host = new EventQueryHost();
        var attackerIds = new[] { "allowed" };
        var dispatched = new List<AttackTestEvent>();

        var handle = await host.Query<AttackTestEvent>()
            .Where(e => attackerIds.Contains(e.AttackerId))
            .SubscribeAsync((e, _) =>
            {
                dispatched.Add(e);
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        var blocked = new AttackTestEvent("blocked", "target", 1, 1);

        await host.PublishAsync(blocked, context);
        Assert.Empty(dispatched);

        if (handle.Document.Filter.Values is QueryValue[] exposedValues)
        {
            exposedValues[0] = QueryValue.FromString("blocked");
        }

        await host.PublishAsync(blocked, context);

        Assert.Empty(dispatched);
    }
}
