using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class MemberValueReaderDeclaredTypeTests
{
    [Fact]
    public void Declared_roots_preserve_base_members_while_default_readers_keep_runtime_lookup()
    {
        var value = new HiddenEvent();

        Assert.Equal(7, new MemberValueReader(typeof(BaseEvent)).Read(value, "Value"));
        Assert.Equal(99, new MemberValueReader().Read(value, "Value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_primitives_and_authored_queries_read_the_same_interface_member(bool compiled)
    {
        IEvent value = new ExplicitEvent();
        var reader = new MemberValueReader(typeof(IEvent));
        var filter = QueryFilter.Compare("Value", QueryComparisonOperator.Equal, QueryValue.FromInteger(7));
        var evaluate = compiled
            ? QueryFilterCompiler.Compile(filter, reader)
            : target => QueryFilterEvaluator.Evaluate(filter, target, reader);
        Assert.True(evaluate(value));
        var manualProjection = Assert.IsType<int>(reader.Read(value, "Value"));
        var values = new List<int>();
        var host = new EventQueryHost();
        using var handle = await host.Query<IEvent>().Where(e => e.Value == 7).Select(e => e.Value)
            .SubscribeAsync((item, _) =>
            {
                values.Add(item);
                return ValueTask.CompletedTask;
            });

        await host.PublishAsync(value, new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));

        Assert.Equal(manualProjection, Assert.Single(values));
    }

    [Fact]
    public void Boxed_value_type_roots_are_supported()
    {
        var value = new KeyValuePair<string, int>("key", 7);
        var reader = new MemberValueReader(typeof(KeyValuePair<string, int>));

        Assert.Equal("key", reader.Read(value, "Key"));
        Assert.Equal(7, reader.Read(value, "Value"));
    }

    [Fact]
    public void Incompatible_targets_fail_with_an_argument_error()
    {
        var reader = new MemberValueReader(typeof(BaseEvent));

        var error = Assert.Throws<ArgumentException>(() => reader.Read(new object(), "Value"));

        Assert.Equal("target", error.ParamName);
    }

    [Fact]
    public void Null_declared_type_is_rejected()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new MemberValueReader(null!));

        Assert.Equal("rootType", error.ParamName);
    }

    [Theory]
    [InlineData("Void")]
    [InlineData("OpenGeneric")]
    [InlineData("GenericParameter")]
    [InlineData("ByRef")]
    [InlineData("Pointer")]
    [InlineData("ByRefLike")]
    public void Unusable_declared_types_are_rejected_at_construction(string shape)
    {
        var type = shape switch
        {
            "Void" => typeof(void),
            "OpenGeneric" => typeof(List<>),
            "GenericParameter" => typeof(List<>).GetGenericArguments()[0],
            "ByRef" => typeof(int).MakeByRefType(),
            "Pointer" => typeof(int).MakePointerType(),
            "ByRefLike" => typeof(Span<int>),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var error = Assert.Throws<ArgumentException>(() => new MemberValueReader(type));

        Assert.Equal("rootType", error.ParamName);
    }

    private class BaseEvent
    {
        public int Value => 7;
    }

    private sealed class HiddenEvent : BaseEvent
    {
        public new int Value => 99;
    }

    private interface IEvent
    {
        int Value { get; }
    }

    private sealed class ExplicitEvent : IEvent
    {
        public int Value => 99;
        int IEvent.Value => 7;
    }
}
