using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class MemberValueReaderContractTests
{
    [Theory]
    [InlineData("WriteOnly")]
    [InlineData("HiddenGetter")]
    [InlineData("Item")]
    public void Read_RejectsPropertiesWithoutAPublicParameterlessGetter(string path)
    {
        var reader = new MemberValueReader();

        var error = Assert.Throws<InvalidOperationException>(() => reader.Read(new Sample(), path));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsOverloadedIndexersWithAQueryError()
    {
        var reader = new MemberValueReader();

        var error = Assert.Throws<InvalidOperationException>(() => reader.Read(new OverloadedIndexers(), "Item"));

        Assert.Contains("Item", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Property")]
    [InlineData("Field")]
    [InlineData("Child.Property")]
    public void Read_PublicPropertiesAndFieldsRemainReadable(string path)
    {
        var reader = new MemberValueReader();
        var sample = new Sample();

        Assert.Equal(7, reader.Read(sample, path));
        Assert.Equal(7, reader.Read(sample, path));
    }

    [Fact]
    public void Read_PublicGetterFailureStillReturnsNull()
    {
        var reader = new MemberValueReader();

        Assert.Null(reader.Read(new Sample(), nameof(Sample.Throwing)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filter_InvalidMemberRaisesTheExpectedQueryError(bool compiled)
    {
        var reader = new MemberValueReader();
        var filter = QueryFilter.Compare("Item", QueryComparisonOperator.Equal, QueryValue.FromInteger(7));
        var evaluate = compiled
            ? QueryFilterCompiler.Compile(filter, reader)
            : target => QueryFilterEvaluator.Evaluate(filter, target, reader);

        Assert.Throws<InvalidOperationException>(() => evaluate(new Sample()));
    }

    private sealed class Sample
    {
        public int Property { get; private set; } = 7;
        public readonly int Field = 7;
        public ChildSample Child { get; } = new();
        public int Throwing => throw new InvalidOperationException("Getter failed.");
        public int this[int index] => throw new InvalidOperationException("An index is required.");

        public int WriteOnly
        {
            set { }
        }

        public int HiddenGetter
        {
            private get => throw new InvalidOperationException("Getter is not public.");
            set { }
        }
    }

    private sealed class ChildSample
    {
        public int Property => 7;
    }

    private sealed class OverloadedIndexers
    {
        public int this[int index] => 7;
        public int this[string index] => 7;
    }
}
