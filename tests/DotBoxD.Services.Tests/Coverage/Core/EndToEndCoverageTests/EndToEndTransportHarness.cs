using System.Net;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Transports.NamedPipes;
using DotBoxD.Transports.Tcp;
using Shared;
using static DotBoxD.Services.Tests.Coverage.Core.EndToEndCoverageTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal sealed class EndToEndTransportHarness : IAsyncDisposable
{
    private readonly RpcHost _host;
    private readonly IAsyncDisposable _clientTransport;
    private readonly RpcPeer _client;

    public IGameService Game { get; }

    private EndToEndTransportHarness(
        RpcHost host,
        IAsyncDisposable clientTransport,
        RpcPeer client,
        IGameService game)
    {
        _host = host;
        _clientTransport = clientTransport;
        _client = client;
        Game = game;
    }

    public static async Task<EndToEndTransportHarness> StartTcpAsync(Action<RpcPeer> configureServer)
    {
        var serverTransport = new TcpServerTransport(IPAddress.Loopback, 0);
        var host = RpcHost
            .Listen(serverTransport, NewSerializer())
            .ForEachPeer(configureServer);
        await host.StartAsync().WaitAsync(EndToEndTimeout).ConfigureAwait(false);

        var port = serverTransport.LocalEndpoint?.Port
            ?? throw new InvalidOperationException("TCP server did not expose a bound port.");

        var clientTransport = new TcpTransport("127.0.0.1", port);
        await clientTransport.ConnectAsync().WaitAsync(EndToEndTimeout).ConfigureAwait(false);
        var client = RpcPeer.Over(clientTransport.Connection!, NewSerializer(), ClientOptions()).Start();
        return new EndToEndTransportHarness(host, clientTransport, client, client.GetGameService());
    }

    public static async Task<EndToEndTransportHarness> StartNamedPipeAsync(Action<RpcPeer> configureServer)
    {
        var pipeName = "dotboxd-e2e-" + Guid.NewGuid().ToString("N");
        var host = RpcHost
            .Listen(new NamedPipeServerTransport(pipeName), NewSerializer())
            .ForEachPeer(configureServer);
        await host.StartAsync().WaitAsync(EndToEndTimeout).ConfigureAwait(false);

        var clientTransport = new NamedPipeClientTransport(pipeName);
        await clientTransport.ConnectAsync().WaitAsync(EndToEndTimeout).ConfigureAwait(false);
        var client = RpcPeer.Over(clientTransport.Connection!, NewSerializer(), ClientOptions()).Start();
        return new EndToEndTransportHarness(host, clientTransport, client, client.GetGameService());
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        await _clientTransport.DisposeAsync().ConfigureAwait(false);
        await _host.DisposeAsync().ConfigureAwait(false);
    }
}
