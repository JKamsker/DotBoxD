using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal static class HostCoverageTestSupport
{
    internal static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan Timeout10s = TimeSpan.FromSeconds(10);

    internal static MessagePackRpcSerializer NewSerializer() => new();

    internal static RpcPeerOptions ClientOptions() =>
        new() { RequestTimeout = Timeout5s };
}
