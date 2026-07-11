using DotBoxD.Codecs.MessagePack;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal static class ProtocolFramingTestSupport
{
    internal static readonly TimeSpan FramingTimeout = TimeSpan.FromSeconds(15);

    internal static MessagePackRpcSerializer NewSerializer() => new();
}
