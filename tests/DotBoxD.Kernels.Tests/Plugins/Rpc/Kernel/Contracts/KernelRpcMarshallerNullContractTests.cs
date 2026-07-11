using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcMarshallerNullContractTests
{
    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(string))]
    public void FromSandboxValue_rejects_null_value_at_public_boundary(Type targetType)
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => KernelRpcMarshaller.FromSandboxValue(null!, targetType));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void FromSandboxValue_rejects_null_type_at_public_boundary()
    {
        var value = SandboxValue.FromInt32(1);

        var exception = Assert.Throws<ArgumentNullException>(
            () => KernelRpcMarshaller.FromSandboxValue(value, null!));

        Assert.Equal("type", exception.ParamName);
    }
}
