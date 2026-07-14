using System.Net;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class TcpServerTransportConstructorCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Constructor_PortOnly_BindsOnAnyAddress()
    {
        await using var server = new TcpServerTransport(0);
        await server.StartAsync().WaitAsync(Timeout);

        Assert.NotNull(server.LocalEndpoint);
        Assert.True(server.LocalEndpoint!.Port > 0);
    }

    [Fact]
    public async Task Constructor_StringAddress_ParsesAndBinds()
    {
        await using var server = new TcpServerTransport("127.0.0.1", 0);
        await server.StartAsync().WaitAsync(Timeout);

        Assert.NotNull(server.LocalEndpoint);
        Assert.Equal(IPAddress.Loopback, server.LocalEndpoint!.Address);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidStringAddress_ThrowsArgumentExceptionForPublicParameter(string address)
    {
        var ex = Assert.Throws<ArgumentException>(() => new TcpServerTransport(address, 0));
        Assert.Equal("address", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullAddress_ThrowsArgumentNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new TcpServerTransport((IPAddress)null!, 0));
        Assert.Equal("address", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullStringAddress_ThrowsArgumentNullForPublicParameter()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new TcpServerTransport((string)null!, 0));
        Assert.Equal("address", ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Constructor_IpAddressPortOutsideTcpRange_ThrowsArgumentOutOfRange(int port)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new TcpServerTransport(IPAddress.Loopback, port));
        Assert.Equal("port", ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Constructor_StringAddressPortOutsideTcpRange_ThrowsArgumentOutOfRange(int port)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new TcpServerTransport("127.0.0.1", port));
        Assert.Equal("port", ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Constructor_PortOnlyOutsideTcpRange_ThrowsArgumentOutOfRange(int port)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new TcpServerTransport(port));
        Assert.Equal("port", ex.ParamName);
    }
}
