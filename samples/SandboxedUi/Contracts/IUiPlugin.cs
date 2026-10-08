using DotBoxD.Services.Attributes;
using MessagePack;

namespace Examples.SandboxedUi.Contracts;

[RpcService]
public interface IUiPlugin
{
    ValueTask<string> GetPackageAsync(CancellationToken cancellationToken = default);
    ValueTask<SearchReply> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}

[MessagePackObject]
public sealed record SearchRequest(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] long Version,
    [property: Key(2)] int EndpointId,
    [property: Key(3)] string Query);

[MessagePackObject]
public sealed record SearchReply(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] long Version,
    [property: Key(2)] string Results);
