using DotBoxD.Transports.NamedPipes;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class NamedPipeTransportNullNameContractTests
{
    [Fact]
    public void Client_constructor_rejects_null_pipe_name_at_public_boundary()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new NamedPipeClientTransport((string)null!));

        Assert.Equal("pipeName", ex.ParamName);
    }

    [Fact]
    public void Client_constructor_rejects_null_server_name_at_public_boundary()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new NamedPipeClientTransport(serverName: null!, pipeName: "some-pipe"));

        Assert.Equal("serverName", ex.ParamName);
    }

    [Fact]
    public void Server_constructor_rejects_null_pipe_name_at_public_boundary()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new NamedPipeServerTransport(null!));

        Assert.Equal("pipeName", ex.ParamName);
    }
}
