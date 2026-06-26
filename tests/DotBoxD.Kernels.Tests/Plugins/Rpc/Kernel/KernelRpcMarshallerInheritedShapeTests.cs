using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// The decode-side record shape (<c>KernelRpcMarshaller.GetRecordShape</c>) must match the encoder exactly: the
/// convention event adapter and the analyzer both walk the type hierarchy base-first and only take properties
/// with a public getter, so an inherited wire property is a real field and a public-setter/non-public-getter
/// property is not. These tests pin both rules on the runtime marshaller (issues #13 and #25).
/// </summary>
public sealed class KernelRpcMarshallerInheritedShapeTests
{
    [Fact]
    public void FromSandboxValue_round_trips_inherited_record_fields()
    {
        // The convention event adapter encodes inherited public properties base-first, so the wire record has
        // both fields. Decode must see the same two-field shape, not just the leaf-declared one.
        var sandbox = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)]);

        var decoded = Assert.IsType<DerivedShape>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(DerivedShape)));

        Assert.Equal(1, decoded.BaseValue);
        Assert.Equal(2, decoded.DerivedValue);
    }

    [Fact]
    public void ToSandboxValue_writes_inherited_record_fields_base_first()
    {
        var value = new DerivedShape { BaseValue = 1, DerivedValue = 2 };

        var sandbox = KernelRpcMarshaller.ToSandboxValue(value, typeof(DerivedShape));

        var record = Assert.IsType<RecordValue>(sandbox);
        Assert.Equal(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)],
            record.Fields);
    }

    [Fact]
    public void SandboxTypeOf_includes_inherited_record_fields()
        => Assert.Equal(
            SandboxType.Record([SandboxType.I32, SandboxType.I32]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(DerivedShape)));

    [Fact]
    public void FromSandboxValue_excludes_property_with_non_public_getter()
    {
        // A property with a public setter/init but a non-public getter is excluded by the analyzer and the
        // convention adapter, so the decode shape is a single field — not two.
        var sandbox = SandboxValue.FromRecord([SandboxValue.FromInt32(5)]);

        var decoded = Assert.IsType<PublicSetterPrivateGetterDto>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(PublicSetterPrivateGetterDto)));

        Assert.Equal(5, decoded.Id);
    }

    [Fact]
    public void SandboxTypeOf_excludes_property_with_non_public_getter()
        => Assert.Equal(
            SandboxType.Record([SandboxType.I32]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(PublicSetterPrivateGetterDto)));

    private abstract class BaseShape
    {
        public int BaseValue { get; init; }
    }

    private sealed class DerivedShape : BaseShape
    {
        public int DerivedValue { get; init; }
    }

    private sealed class PublicSetterPrivateGetterDto
    {
        public int Id { get; set; }

        public string Secret { private get; set; } = string.Empty;
    }
}
