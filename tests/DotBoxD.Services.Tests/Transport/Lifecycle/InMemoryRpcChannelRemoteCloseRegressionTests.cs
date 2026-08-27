using DotBoxD.Services.Testing;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class InMemoryRpcChannelRemoteCloseRegressionTests
{
    [Fact]
    public async Task New_pair_reports_both_endpoints_connected()
    {
        var (first, second) = InMemoryRpcChannel.CreatePair();
        await using var firstEndpoint = first;
        await using var secondEndpoint = second;

        Assert.True(firstEndpoint.IsConnected);
        Assert.True(secondEndpoint.IsConnected);
    }

    [Fact]
    public async Task Remote_close_terminal_marks_surviving_endpoint_disconnected()
    {
        var (closedByPeer, surviving) = InMemoryRpcChannel.CreatePair();
        await using var survivingEndpoint = surviving;

        await closedByPeer.DisposeAsync();
        using var closePayload = await survivingEndpoint.ReceiveAsync();

        Assert.Equal(0, closePayload.Length);
        Assert.False(survivingEndpoint.IsConnected);
    }
}
