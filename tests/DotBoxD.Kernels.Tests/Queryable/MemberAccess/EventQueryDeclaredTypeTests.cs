using System.Linq.Expressions;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryDeclaredTypeTests
{
    [Theory]
    [InlineData("Implicit")]
    [InlineData("Explicit")]
    [InlineData("PublicShadow")]
    [InlineData("InheritedExplicit")]
    public async Task Interface_queries_read_the_authored_interface_member(string shape)
    {
        IChildEvent value = shape switch
        {
            "Implicit" => new ImplicitEvent(),
            "Explicit" => new ExplicitEvent(),
            "PublicShadow" => new ShadowedInterfaceEvent(),
            "InheritedExplicit" => new InheritedExplicitEvent(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        await AssertQuery(value, e => e.Value == 7, e => e.Value, [7]);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(99)]
    public async Task Base_class_queries_preserve_hidden_property_identity(int expectedValue)
    {
        BaseEvent value = new HiddenPropertyEvent();

        await AssertQuery(value, e => e.Value == expectedValue, e => e.Value, expectedValue == 7 ? [7] : []);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(99)]
    public async Task Base_class_queries_preserve_hidden_field_identity(int expectedValue)
    {
        BaseFieldEvent value = new HiddenFieldEvent();

        await AssertQuery(value, e => e.Value == expectedValue, e => e.Value, expectedValue == 7 ? [7] : []);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(99)]
    public async Task Base_class_queries_keep_virtual_dispatch(int expectedValue)
    {
        VirtualEvent value = new OverriddenEvent();

        await AssertQuery(value, e => e.Value == expectedValue, e => e.Value, expectedValue == 7 ? [7] : []);
    }

    private static async Task AssertQuery<TEvent>(
        TEvent value,
        Expression<Func<TEvent, bool>> filter,
        Expression<Func<TEvent, int>> project,
        int[] expected)
    {
        var host = new EventQueryHost();
        var values = new List<int>();
        using var handle = await host.Query<TEvent>().Where(filter).Select(project)
            .SubscribeAsync((item, _) =>
            {
                values.Add(item);
                return ValueTask.CompletedTask;
            });
        await host.PublishAsync(value, new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));

        Assert.Equal(expected, values);
        Assert.Equal(expected.Length, handle.Dispatches);
    }

    private interface IBaseEvent
    {
        int Value { get; }
    }

    private interface IChildEvent : IBaseEvent;

    private sealed class ImplicitEvent : IChildEvent
    {
        public int Value => 7;
    }

    private class ExplicitEvent : IChildEvent
    {
        int IBaseEvent.Value => 7;
    }

    private sealed class InheritedExplicitEvent : ExplicitEvent;

    private sealed class ShadowedInterfaceEvent : IChildEvent
    {
        public int Value => 99;
        int IBaseEvent.Value => 7;
    }

    private class BaseEvent
    {
        public int Value => 7;
    }

    private sealed class HiddenPropertyEvent : BaseEvent
    {
        public new int Value => 99;
    }

    private class BaseFieldEvent
    {
        public readonly int Value = 7;
    }

    private sealed class HiddenFieldEvent : BaseFieldEvent
    {
        public new int Value => 99;
    }

    private class VirtualEvent
    {
        public virtual int Value => 99;
    }

    private sealed class OverriddenEvent : VirtualEvent
    {
        public override int Value => 7;
    }
}
