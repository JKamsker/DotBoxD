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
}
