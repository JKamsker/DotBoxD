using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Core.ValueValidation;

public sealed class FlatBuiltInScalarListValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    public void Flat_I32_list_rejects_wrong_item_at_each_position(int invalidIndex)
    {
        var values = I32Values(9);
        values[invalidIndex] = SandboxValue.FromString("wrong");

        AssertInvalid(
            new ListValue(values, SandboxType.I32),
            SandboxType.List(SandboxType.I32));
    }

    [Fact]
    public void Flat_F64_list_rejects_non_finite_item()
        => AssertInvalid(
            new ListValue([new F64Value(double.NaN)], SandboxType.F64),
            SandboxType.List(SandboxType.F64));

    [Fact]
    public void Flat_String_list_rejects_null_item_payload()
        => AssertInvalid(
            new ListValue([CreateMalformedString()], SandboxType.String),
            SandboxType.List(SandboxType.String));

    [Fact]
    public void Flat_SandboxPath_list_rejects_null_item_payload()
        => AssertInvalid(
            new ListValue([CreateMalformed<SandboxPathValue>()], SandboxType.SandboxPath),
            SandboxType.List(SandboxType.SandboxPath));

    [Fact]
    public void Flat_SandboxUri_list_rejects_null_item_payload()
        => AssertInvalid(
            new ListValue([CreateMalformed<SandboxUriValue>()], SandboxType.SandboxUri),
            SandboxType.List(SandboxType.SandboxUri));

    [Fact]
    public void Flat_scalar_list_rejects_declared_and_expected_item_type_mismatch()
        => AssertInvalid(
            new ListValue([SandboxValue.FromInt32(1)], SandboxType.I32),
            SandboxType.List(SandboxType.String));

    [Fact]
    public void Flat_scalar_list_accepts_equivalent_noncanonical_item_types()
    {
        var declaredItemType = SandboxType.Scalar("I32");
        var expectedItemType = SandboxType.Scalar("I32");

        SandboxValueValidator.RequireType(
            new ListValue([SandboxValue.FromInt32(1)], declaredItemType),
            SandboxType.List(expectedItemType),
            "bad input");
    }

    [Fact]
    public void Nested_list_item_uses_recursive_validation()
    {
        var itemType = SandboxType.List(SandboxType.I32);
        var malformedItem = new ListValue([SandboxValue.FromString("wrong")], SandboxType.I32);

        AssertInvalid(
            new ListValue([malformedItem], itemType),
            SandboxType.List(itemType));
    }

    [Fact]
    public void Record_item_uses_recursive_validation()
    {
        var itemType = SandboxType.Record([SandboxType.I32]);
        var malformedItem = SandboxValue.FromRecord([SandboxValue.FromString("wrong")]);

        AssertInvalid(
            new ListValue([malformedItem], itemType),
            SandboxType.List(itemType));
    }

    [Fact]
    public void Map_item_uses_recursive_validation()
    {
        var itemType = SandboxType.Map(SandboxType.String, SandboxType.I32);
        var malformedItem = new MapValue(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("key")] = SandboxValue.FromString("wrong")
            },
            SandboxType.String,
            SandboxType.I32);

        AssertInvalid(
            new ListValue([malformedItem], itemType),
            SandboxType.List(itemType));
    }

    [Fact]
    public void Opaque_item_uses_recursive_validation()
    {
        var itemType = SandboxType.Scalar("PlayerId");
        var malformedItem = new OpaqueIdValue("PlayerId", "not a safe id");

        AssertInvalid(
            new ListValue([malformedItem], itemType),
            SandboxType.List(itemType));
    }

    private static SandboxValue[] I32Values(int count)
    {
        var values = new SandboxValue[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = SandboxValue.FromInt32(i);
        }

        return values;
    }

    private static StringValue CreateMalformedString()
        => (StringValue)RuntimeHelpers.GetUninitializedObject(typeof(StringValue));

    private static T CreateMalformed<T>()
        where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void AssertInvalid(SandboxValue value, SandboxType expectedType)
    {
        var exception = Assert.Throws<SandboxRuntimeException>(
            () => SandboxValueValidator.RequireType(value, expectedType, "bad input"));

        Assert.Equal(SandboxErrorCode.InvalidInput, exception.Error.Code);
        Assert.Equal("bad input", exception.Error.SafeMessage);
    }
}
