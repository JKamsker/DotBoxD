using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxValueFactoryTests
{
    private sealed class NullEntryDictionary(
        SandboxValue? entryKey,
        SandboxValue? entryValue) : IReadOnlyDictionary<SandboxValue, SandboxValue>
    {
        public IEnumerable<SandboxValue> Keys
        {
            get { yield return entryKey!; }
        }

        public IEnumerable<SandboxValue> Values
        {
            get { yield return entryValue!; }
        }

        public int Count => 1;

        public SandboxValue this[SandboxValue key] => entryValue!;

        public bool ContainsKey(SandboxValue key) => false;

        public bool TryGetValue(SandboxValue key, out SandboxValue value)
        {
            value = entryValue!;
            return false;
        }

        public IEnumerator<KeyValuePair<SandboxValue, SandboxValue>> GetEnumerator()
        {
            yield return new KeyValuePair<SandboxValue, SandboxValue>(entryKey!, entryValue!);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    [Fact]
    public void Collection_factories_reject_null_type_operands()
    {
        Assert.ThrowsAny<ArgumentException>(() => SandboxValue.FromList([], null!));
        Assert.ThrowsAny<ArgumentException>(() =>
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>(),
                null!,
                SandboxType.I32));
        Assert.ThrowsAny<ArgumentException>(() =>
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>(),
                SandboxType.String,
                null!));
    }

    [Fact]
    public void Collection_factories_reject_null_elements()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            SandboxValue.FromList([null!], SandboxType.I32));
        Assert.ThrowsAny<ArgumentException>(() =>
            SandboxValue.FromMap(
                new NullEntryDictionary(null, SandboxValue.FromInt32(1)),
                SandboxType.String,
                SandboxType.I32));
        Assert.ThrowsAny<ArgumentException>(() =>
            SandboxValue.FromMap(
                new NullEntryDictionary(SandboxValue.FromString("key"), null),
                SandboxType.String,
                SandboxType.I32));
        Assert.ThrowsAny<ArgumentException>(() => SandboxValue.FromRecord([null!]));
    }

    [Fact]
    public void FromBool_reuses_immutable_bool_values()
    {
        var firstTrue = SandboxValue.FromBool(true);
        var secondTrue = SandboxValue.FromBool(true);
        var firstFalse = SandboxValue.FromBool(false);
        var secondFalse = SandboxValue.FromBool(false);

        Assert.Same(firstTrue, secondTrue);
        Assert.Same(firstFalse, secondFalse);
        Assert.NotSame(firstTrue, firstFalse);
        Assert.Equal(new BoolValue(true), firstTrue);
        Assert.Equal(new BoolValue(false), firstFalse);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(256)]
    public void FromInt32_reuses_common_immutable_i32_values(int value)
    {
        var first = SandboxValue.FromInt32(value);
        var second = SandboxValue.FromInt32(value);

        Assert.Same(first, second);
        Assert.Equal(new I32Value(value), first);
    }

    [Fact]
    public void Built_in_value_types_reuse_singleton_sandbox_types()
    {
        var path = SandboxValue.FromPath("config/settings.json");
        var uri = SandboxValue.FromUri("https://example.test/config");

        Assert.Same(SandboxType.SandboxPath, path.Type);
        Assert.Same(SandboxType.SandboxUri, uri.Type);
    }

    [Fact]
    public void String_values_reject_null_payloads_at_public_boundary()
    {
        var factoryEx = Assert.Throws<ArgumentNullException>(() => SandboxValue.FromString(null!));
        var constructorEx = Assert.Throws<ArgumentNullException>(() => new StringValue(null!));
        var initializerEx = Assert.Throws<ArgumentNullException>(() => new StringValue("ok") { Value = null! });

        Assert.Equal("value", factoryEx.ParamName);
        Assert.Equal("Value", constructorEx.ParamName);
        Assert.Equal("value", initializerEx.ParamName);
    }
}
