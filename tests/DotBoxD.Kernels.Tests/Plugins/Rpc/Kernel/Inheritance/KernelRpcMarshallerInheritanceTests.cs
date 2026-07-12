using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcMarshallerInheritanceTests
{
    [Fact]
    public void ToSandboxValue_encodes_inherited_dto_properties_base_first()
    {
        var dto = new DerivedDto(10, 11, true);

        var encoded = KernelRpcMarshaller.ToSandboxValue(dto, typeof(DerivedDto));

        Assert.Equal(
            SandboxValue.FromRecord(
            [
                SandboxValue.FromInt32(10),
                SandboxValue.FromInt32(11),
                SandboxValue.FromBool(true)
            ]),
            encoded);
    }

    [Fact]
    public void FromSandboxValue_decodes_inherited_dto_properties()
    {
        var sandbox = SandboxValue.FromRecord(
            [
                SandboxValue.FromInt32(10),
                SandboxValue.FromInt32(11),
                SandboxValue.FromBool(false)
            ]);

        var dto = Assert.IsType<DerivedDto>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(DerivedDto)));

        Assert.Equal(10, dto.BaseId);
        Assert.Equal(11, dto.MonsterId);
        Assert.False(dto.Success);
    }

    [Fact]
    public void SandboxValue_round_trip_preserves_inherited_public_fields()
    {
        var original = new DerivedFieldDto { BaseId = 20, MonsterId = 21 };

        var encoded = KernelRpcMarshaller.ToSandboxValue(original, typeof(DerivedFieldDto));
        var decoded = Assert.IsType<DerivedFieldDto>(
            KernelRpcMarshaller.FromSandboxValue(encoded, typeof(DerivedFieldDto)));

        Assert.Equal(20, decoded.BaseId);
        Assert.Equal(21, decoded.MonsterId);
    }

    [Fact]
    public void SandboxTypeOf_rejects_hidden_inherited_data_members()
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(HiddenDerivedDto)));

        Assert.Contains("multiple public data members named 'Id'", exception.Message, StringComparison.Ordinal);
    }

    private abstract class BaseDto(int baseId)
    {
        public int BaseId { get; } = baseId;
    }

    private sealed class DerivedDto(int baseId, int monsterId, bool success) : BaseDto(baseId)
    {
        public int MonsterId { get; } = monsterId;

        public bool Success { get; } = success;
    }

    private abstract class BaseFieldDto
    {
        public int BaseId;
    }

    private sealed class DerivedFieldDto : BaseFieldDto
    {
        public int MonsterId;
    }

    private abstract class HiddenBaseDto
    {
        public int Id { get; init; }
    }

    private sealed class HiddenDerivedDto : HiddenBaseDto
    {
        public new int Id { get; init; }
    }
}
