namespace DotBoxD.Kernels.Tests.Core.Contracts;

public sealed class ExecutionPlanSealEqualityTests
{
    [Theory]
    [InlineData("sample", "sample", true)]
    [InlineData("sample", "Sample", false)]
    [InlineData("sample", "samPle", false)]
    [InlineData("sample", "samplE", false)]
    [InlineData("sample", "samples", false)]
    [InlineData("sample", "sample ", false)]
    [InlineData("a\0b", "a\0b", true)]
    [InlineData("a\0b", "ab", false)]
    [InlineData("é", "e\u0301", false)]
    [InlineData("😀", "😀", true)]
    [InlineData("😀", "😁", false)]
    [InlineData("\u0100", "\0", false)]
    public void Equality_preserves_ordinal_string_values(string left, string right, bool expected)
    {
        var first = new ExecutionPlanSeal(left);
        var second = new ExecutionPlanSeal(new string(right.AsSpan()));

        Assert.Equal(expected, first.Equals(second));
        Assert.Equal(expected, second.Equals(first));
        Assert.Equal(expected, first.Equals((object)second));
        Assert.Equal(expected, EqualityComparer<ExecutionPlanSeal>.Default.Equals(first, second));
        if (expected)
        {
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }
    }

    [Fact]
    public void Equality_preserves_unpaired_surrogate_code_units()
    {
        string[] values = ["\uD800", "\uD801", "\uDC00", "\uDC01", "\uFFFD", "x\uD800", "x\uDC00"];
        foreach (var left in values)
        {
            foreach (var right in values)
            {
                var first = new ExecutionPlanSeal(left);
                var second = new ExecutionPlanSeal(new string(right.AsSpan()));
                Assert.Equal(StringComparer.Ordinal.Equals(left, right), first.Equals(second));
            }
        }
    }

    [Fact]
    public void Equality_preserves_identity_and_rejects_null_or_other_types()
    {
        var seal = new ExecutionPlanSeal("sample");
        Assert.True(seal.Equals(seal));
        Assert.False(seal.Equals((ExecutionPlanSeal?)null));
        Assert.False(seal.Equals((object?)null));
        Assert.False(seal.Equals((object)"sample"));
    }

    [Fact]
    public void Equal_values_share_hash_set_identity()
    {
        var first = new ExecutionPlanSeal("sample");
        var equal = new ExecutionPlanSeal(new string("sample".AsSpan()));
        var different = new ExecutionPlanSeal("Sample");
        var seals = new HashSet<ExecutionPlanSeal> { first };

        Assert.False(seals.Add(equal));
        Assert.Contains(equal, seals);
        Assert.True(seals.Add(different));
        Assert.Equal(2, seals.Count);
    }

    [Fact]
    public void Text_representation_remains_redacted()
        => Assert.Equal("[redacted]", new ExecutionPlanSeal("sample").ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u2003")]
    public void Constructor_preserves_blank_value_validation(string? value)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => new ExecutionPlanSeal(value!));
        Assert.Equal("value", error.ParamName);
    }
}
