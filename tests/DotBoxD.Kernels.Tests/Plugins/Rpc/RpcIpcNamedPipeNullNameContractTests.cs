using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class RpcIpcNamedPipeNullNameContractTests
{
    [Fact]
    public void ListenNamedPipe_rejects_null_pipe_name_at_public_boundary()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => RpcMessagePackIpc.ListenNamedPipe(
                null!,
                _ => { },
                NamedPipeTransportOptions.UnsafeDevelopment));

        Assert.Equal("pipeName", ex.ParamName);
    }

    [Fact]
    public void ConnectNamedPipeAsync_rejects_null_pipe_name_at_public_boundary()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = RpcMessagePackIpc.ConnectNamedPipeAsync(
                    null!,
                    NamedPipeTransportOptions.UnsafeDevelopment);
            });

        Assert.Equal("pipeName", ex.ParamName);
    }
}
