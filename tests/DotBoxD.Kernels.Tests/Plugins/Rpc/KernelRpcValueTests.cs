using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcValueTests
{
    [Fact]
    public void String_rejects_null_text()
        => Assert.Throws<ArgumentNullException>(() => KernelRpcValue.String(null!));

    [Fact]
    public void List_rejects_null_items()
        => Assert.Throws<ArgumentNullException>(() => KernelRpcValue.List(null!));

    [Fact]
    public void Record_rejects_null_fields()
        => Assert.Throws<ArgumentNullException>(() => KernelRpcValue.Record(null!));
}
