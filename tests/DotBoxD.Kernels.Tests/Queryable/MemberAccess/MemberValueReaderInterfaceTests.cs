using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class MemberValueReaderInterfaceTests
{
    [Theory]
    [InlineData("Inherited", false)]
    [InlineData("Inherited", true)]
    [InlineData("Diamond", false)]
    [InlineData("Diamond", true)]
    [InlineData("Hidden", false)]
    [InlineData("Hidden", true)]
    [InlineData("Default", false)]
    [InlineData("Default", true)]
    [InlineData("Declared", false)]
    [InlineData("Declared", true)]
    public void Read_resolves_interface_properties_and_preserves_null_chains(string shape, bool nullSource)
    {
        var reader = new MemberValueReader();
        var source = new ValueSource(7);
        object target = shape switch
        {
            "Inherited" => new Payload<IChild>(nullSource ? null! : source),
            "Diamond" => new Payload<IDiamond>(nullSource ? null! : source),
            "Hidden" => new Payload<IHiddenLeaf>(nullSource ? null! : new HiddenSource()),
            "Default" => new Payload<IDefaultChild>(nullSource ? null! : new DefaultSource()),
            "Declared" => new Payload<IHidden>(nullSource ? null! : new HiddenSource()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        object? expected = nullSource ? null : shape is "Hidden" or "Declared" ? "hidden" : 7;

        Assert.Equal(expected, reader.Read(target, "Source.Value"));
        Assert.Equal(expected, reader.Read(target, "Source.Value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_rejects_unrelated_inherited_declarations_as_ambiguous(bool sameType)
    {
        object target = sameType
            ? new Payload<IAmbiguousSame>(new AmbiguousSource())
            : new Payload<IAmbiguousDifferent>(new AmbiguousSource());
        var error = Assert.Throws<InvalidOperationException>(() => new MemberValueReader().Read(target, "Source.Value"));

        Assert.Contains("ambiguous", error.Message, StringComparison.Ordinal);
        Assert.Contains("Source.Value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Covariance_does_not_hide_unrelated_property_declarations()
    {
        var target = new Payload<IAmbiguousCovariant>(null!);
        var error = Assert.Throws<InvalidOperationException>(() => new MemberValueReader().Read(target, "Source.Value"));

        Assert.Contains("ambiguous", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Source.Value")]
    [InlineData("Source.Item")]
    public void Inherited_properties_still_require_public_parameterless_getters(string path)
    {
        var target = new Payload<IUnreadableChild>(null!);
        var error = Assert.Throws<InvalidOperationException>(() => new MemberValueReader().Read(target, path));

        Assert.Contains("public parameterless getter", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inherited_getter_failure_still_returns_null()
    {
        var target = new Payload<IChild>(new ThrowingSource());

        Assert.Null(new MemberValueReader().Read(target, "Source.Value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filters_match_inherited_interface_paths(bool compiled)
    {
        var reader = new MemberValueReader();
        var filter = QueryFilter.Compare("Source.Value", QueryComparisonOperator.Equal, QueryValue.FromInteger(7));
        var evaluate = compiled
            ? QueryFilterCompiler.Compile(filter, reader)
            : target => QueryFilterEvaluator.Evaluate(filter, target, reader);

        Assert.True(evaluate(new Payload<IChild>(new ValueSource(7))));
        Assert.False(evaluate(new Payload<IChild>(new ValueSource(2))));
        Assert.False(evaluate(new Payload<IChild>(null!)));
    }

    [Fact]
    public async Task Authored_query_routes_and_projects_inherited_interface_values()
    {
        var host = new EventQueryHost();
        var values = new List<int>();
        using var handle = await host.Query<Payload<IChild>>()
            .Where(e => e.Source.Value == 7)
            .Select(e => e.Source.Value)
            .SubscribeAsync((value, _) =>
            {
                values.Add(value);
                return ValueTask.CompletedTask;
            });
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        await host.PublishAsync(new Payload<IChild>(new ValueSource(7)), context);
        await host.PublishAsync(new Payload<IChild>(new ValueSource(2)), context);
        await host.PublishAsync(new Payload<IChild>(new ValueSource(7)), context);

        Assert.Equal([7, 7], values);
        Assert.Equal(2, handle.FilterEvaluations);
        Assert.Equal(2, handle.Dispatches);
    }

    private sealed record Payload<T>(T Source);

    private interface IValue
    {
        int Value { get; }
    }

    private interface IChild : IValue;
    private interface ILeft : IValue;
    private interface IRight : IValue;
    private interface IDiamond : ILeft, IRight;

    private sealed class ValueSource(int value) : IChild, IDiamond
    {
        int IValue.Value => value;
    }

    private sealed class ThrowingSource : IChild
    {
        int IValue.Value => throw new InvalidOperationException("Getter failed.");
    }

    private interface IUnreadableValue
    {
        int Value { set; }
        int this[int index] { get; }
    }

    private interface IUnreadableChild : IUnreadableValue;

    private interface IHidden : IValue
    {
        new string Value { get; }
    }

    private interface IHiddenLeaf : IHidden;

    private sealed class HiddenSource : IHiddenLeaf
    {
        int IValue.Value => 1;
        string IHidden.Value => "hidden";
    }

    private interface IDefaultValue
    {
        int Value => 7;
    }

    private interface IDefaultChild : IDefaultValue;
    private sealed class DefaultSource : IDefaultChild;

    private interface ISameValue
    {
        int Value { get; }
    }

    private interface IDifferentValue
    {
        string Value { get; }
    }

    private interface IAmbiguousSame : IValue, ISameValue;
    private interface IAmbiguousDifferent : IValue, IDifferentValue;

    private interface ICovariantValue<out T>
    {
        T Value { get; }
    }

    private interface IAmbiguousCovariant : ICovariantValue<string>, ICovariantValue<object>;

    private sealed class AmbiguousSource : IAmbiguousSame, IAmbiguousDifferent
    {
        int IValue.Value => 1;
        int ISameValue.Value => 2;
        string IDifferentValue.Value => "different";
    }
}
