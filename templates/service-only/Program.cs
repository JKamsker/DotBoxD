using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Testing;
using MessagePack.Resolvers;

var (hostChannel, clientChannel) = InMemoryRpcChannel.CreatePair();
var serializer = MessagePackRpcSerializer.CreateWithResolver(BuiltinResolver.Instance);
await using var host = RpcPeer.Over(hostChannel, serializer).Provide<IGreetingService>(new GreetingService()).Start();
await using var client = RpcPeer.Over(clientChannel, serializer).Start();
Console.WriteLine(await client.Get<IGreetingService>().GreetAsync("DotBoxD"));

[RpcService]
public interface IGreetingService
{
    ValueTask<string> GreetAsync(string name, CancellationToken cancellationToken = default);
}

public sealed class GreetingService : IGreetingService
{
    public ValueTask<string> GreetAsync(string name, CancellationToken cancellationToken = default)
        => ValueTask.FromResult($"Hello, {name}!");
}
