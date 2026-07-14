using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class KernelRpcMarshallerSurpriseTests
{
    [Fact]
    public void ToSandboxValue_wraps_throwing_dto_getter_with_field_context()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(
                new ThrowingGetterDto(),
                typeof(ThrowingGetterDto)));

        Assert.Contains(nameof(ThrowingGetterDto), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ThrowingGetterDto.Value), ex.Message, StringComparison.Ordinal);
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal("getter failed", inner.Message);
    }

    [Fact]
    public void ToSandboxValue_still_writes_regular_dto_fields()
    {
        var sandbox = KernelRpcMarshaller.ToSandboxValue(
            new RegularGetterDto { Value = 42 },
            typeof(RegularGetterDto));

        Assert.Equal(SandboxValue.FromRecord([SandboxValue.FromInt32(42)]), sandbox);
    }

    private sealed class ThrowingGetterDto
    {
        public int Value => throw new InvalidOperationException("getter failed");
    }

    private sealed class RegularGetterDto
    {
        public int Value { get; set; }
    }
}
