using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;
using DotBoxD.Transports.NamedPipes;
using DotBoxD.UI;
using Examples.SandboxedUi.Contracts;
using Examples.SandboxedUi.Plugin;

await using var server = RpcMessagePackIpc.Listen(new NamedPipeServerTransport(args[0], maxMessageSize: 512 * 1024),
    peer => peer.Provide<IUiPlugin>(new UiPlugin()), new RpcPeerOptions { MaxAcceptedPeers = 1, MaxInboundBytes = 512 * 1024 });
await server.StartAsync();
Console.WriteLine("ready");
await Task.Delay(Timeout.InfiniteTimeSpan);

namespace Examples.SandboxedUi.Plugin
{
    internal sealed class UiPlugin : IUiPlugin
    {
        public ValueTask<string> GetPackageAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(UiPackageJson.Export(CounterComponent.Package(), new UiPolicy()));

        public ValueTask<SearchReply> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.EndpointId != 7 || request.Query.Length > 16_384)
            {
                throw new ArgumentException("Invalid search request.");
            }

            // Arbitrary plugin C# executes only in this worker process.
            var results = string.Join(", ", new[] { "apple", "apricot", "banana" }
                .Where(item => item.Contains(request.Query, StringComparison.OrdinalIgnoreCase)));
            return ValueTask.FromResult(new SearchReply(request.SessionId, request.Version, results));
        }
    }
}
