using System.Net;
using System.Net.Sockets;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class TcpServerRestartAcceptTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_preserves_shutdown_cancellation_for_the_previous_accept(bool beforeStartingAccept)
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Guard);
        void Restart()
        {
            server._onFreshAcceptStartedForTest = null;
            server._onPendingAcceptConsumeForTest = null;
            server.StopAsync().GetAwaiter().GetResult();
            server.StartAsync().GetAwaiter().GetResult();
        }
        if (beforeStartingAccept)
        {
            server._onPendingAcceptConsumeForTest = Restart;
        }
        else
        {
            server._onFreshAcceptStartedForTest = Restart;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.AcceptAsync().WaitAsync(Guard));

        Assert.NotNull(server.LocalEndpoint);
    }

    [Fact]
    public async Task Canceled_accept_from_previous_listener_does_not_replace_restarted_accept()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Guard);
        using var cancellation = new CancellationTokenSource();
        server._onFreshAcceptStartedForTest = () =>
        {
            server._onFreshAcceptStartedForTest = null;
            server.StopAsync().GetAwaiter().GetResult();
            server.StartAsync().GetAwaiter().GetResult();
            cancellation.Cancel();
        };

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            server.AcceptAsync(cancellation.Token).WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);

        var endpoint = Assert.IsType<IPEndPoint>(server.LocalEndpoint);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, endpoint.Port).WaitAsync(Guard);
        await using var accepted = await server.AcceptAsync().WaitAsync(Guard);

        Assert.True(accepted.IsConnected);
    }
}
