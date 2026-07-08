using System.Text.Json.Serialization;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class KernelRpcMarshallerSurpriseTests
{
    [Fact]
    public void FromSandboxValue_replays_constructor_assigned_init_property()
    {
        var dto = Assert.IsType<ConstructorInitReplayDto>(
            KernelRpcMarshaller.FromSandboxValue(
                SandboxValue.FromRecord([SandboxValue.FromInt32(-7)]),
                typeof(ConstructorInitReplayDto)));

        Assert.Equal(-7, dto.Id);
    }

    [Fact]
    public void FromKernelRpcValue_replays_constructor_assigned_public_field()
    {
        var dto = Assert.IsType<ConstructorFieldReplayDto>(
            KernelRpcMarshaller.FromKernelRpcValue(
                KernelRpcValue.Record([KernelRpcValue.Int32(-7)]),
                typeof(ConstructorFieldReplayDto)));

        Assert.Equal(-7, dto.Id);
    }

    [Fact]
    public void FromSandboxValue_rejects_constructor_mutated_read_only_property()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(
                SandboxValue.FromRecord([SandboxValue.FromInt32(-7)]),
                typeof(ConstructorReadOnlyReplayDto)));

        Assert.Contains(nameof(ConstructorReadOnlyReplayDto.Id), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromKernelRpcValue_rejects_constructor_mutated_read_only_property()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(
                KernelRpcValue.Record([KernelRpcValue.Int32(-7)]),
                typeof(ConstructorReadOnlyReplayDto)));

        Assert.Contains(nameof(ConstructorReadOnlyReplayDto.Id), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SandboxTypeOf_rejects_setter_dto_with_unmatched_required_constructor()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(ConstructorOnlySetterDto)));

        Assert.Contains(nameof(ConstructorOnlySetterDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains("constructor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromSandboxValue_rejects_setter_dto_with_partially_mapped_required_constructor()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(
                SandboxValue.FromRecord([SandboxValue.FromString("ember")]),
                typeof(PartiallyMappedConstructorSetterDto)));

        Assert.Contains(nameof(PartiallyMappedConstructorSetterDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains("constructor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SandboxTypeOf_rejects_setter_dto_with_optional_only_constructor()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(OptionalOnlyConstructorSetterDto)));

        Assert.Contains(nameof(OptionalOnlyConstructorSetterDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains("constructor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSandboxValue_rejects_setter_dto_with_unmatched_required_constructor()
    {
        var dto = new ConstructorOnlySetterDto(42) { Name = "ember" };

        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(dto, typeof(ConstructorOnlySetterDto)));

        Assert.Contains(nameof(ConstructorOnlySetterDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains("constructor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSandboxValue_rejects_constructor_shifted_read_only_property_before_encoding()
    {
        var dto = new ShiftedReadOnlyDto(5);

        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(dto, typeof(ShiftedReadOnlyDto)));

        Assert.Contains(nameof(ShiftedReadOnlyDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_marshaller_round_trips_reconstructible_read_only_computed_dto()
    {
        var sandbox = KernelRpcMarshaller.ToSandboxValue(
            new ReconstructibleReadOnlyComputedDto(3, 4),
            typeof(ReconstructibleReadOnlyComputedDto));

        Assert.Equal(
            SandboxValue.FromRecord(
                [SandboxValue.FromInt32(3), SandboxValue.FromInt32(4), SandboxValue.FromInt32(7)]),
            sandbox);

        var dto = Assert.IsType<ReconstructibleReadOnlyComputedDto>(
            KernelRpcMarshaller.FromSandboxValue(
                sandbox,
                typeof(ReconstructibleReadOnlyComputedDto)));

        Assert.Equal(3, dto.X);
        Assert.Equal(4, dto.Y);
        Assert.Equal(7, dto.Sum);
    }

    [Fact]
    public void Runtime_marshaller_excludes_serializer_ignored_computed_getters_from_record_shape()
    {
        var jsonIgnored = KernelRpcMarshaller.ToSandboxValue(
            new JsonIgnoredComputedDto(0),
            typeof(JsonIgnoredComputedDto));
        var messagePackIgnored = KernelRpcMarshaller.ToSandboxValue(
            new MessagePackIgnoredComputedDto(0),
            typeof(MessagePackIgnoredComputedDto));

        Assert.Equal(SandboxValue.FromRecord([SandboxValue.FromInt32(0)]), jsonIgnored);
        Assert.Equal(SandboxValue.FromRecord([SandboxValue.FromInt32(0)]), messagePackIgnored);
    }

    [Fact]
    public void ToSandboxValue_accepts_reconstructible_defensive_copy_read_only_array()
    {
        var sandbox = KernelRpcMarshaller.ToSandboxValue(
            new DefensiveCopyReadOnlyArrayDto([1, 2, 3]),
            typeof(DefensiveCopyReadOnlyArrayDto));

        Assert.Equal(
            SandboxValue.FromRecord(
                [
                    SandboxValue.FromList(
                        [
                            SandboxValue.FromInt32(1),
                            SandboxValue.FromInt32(2),
                            SandboxValue.FromInt32(3)
                        ],
                        SandboxType.I32)
                ]),
            sandbox);
    }

    private sealed class ConstructorInitReplayDto(int id)
    {
        public int Id { get; init; } = Math.Abs(id);
    }

    private sealed class ConstructorFieldReplayDto(int id)
    {
        public int Id = Math.Abs(id);
    }

    private sealed class ConstructorReadOnlyReplayDto(int id)
    {
        public int Id { get; } = Math.Abs(id);
    }

    private sealed class ConstructorOnlySetterDto
    {
        public ConstructorOnlySetterDto(int seed)
            => _ = seed;

        public string Name { get; set; } = string.Empty;
    }

    private sealed class PartiallyMappedConstructorSetterDto
    {
        public PartiallyMappedConstructorSetterDto(string name, int seed)
        {
            Name = name;
            _ = seed;
        }

        public string Name { get; set; }
    }

    private sealed class OptionalOnlyConstructorSetterDto
    {
        public OptionalOnlyConstructorSetterDto(int seed = 0)
            => _ = seed;

        public string Name { get; set; } = string.Empty;
    }

    private sealed class ShiftedReadOnlyDto(int id)
    {
        public int Id { get; } = id + 1;
    }

    private sealed class ReconstructibleReadOnlyComputedDto(int x, int y)
    {
        public int X { get; } = x;

        public int Y { get; } = y;

        public int Sum => X + Y;
    }

    private sealed class JsonIgnoredComputedDto(int value)
    {
        public int Value { get; } = value;

        [JsonIgnore]
        public bool IsEmpty => Value == 0;
    }

    private sealed class MessagePackIgnoredComputedDto(int value)
    {
        public int Value { get; } = value;

        [MessagePack.IgnoreMember]
        public bool IsEmpty => Value == 0;
    }

    private sealed class DefensiveCopyReadOnlyArrayDto(int[] values)
    {
        public int[] Values { get; } = values.ToArray();
    }
}
