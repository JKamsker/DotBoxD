using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal static class CoreInternalScenarioTestSupport
{
    internal const string Service = "Svc";
    internal const string Method = "Op";
    internal static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan Timeout10s = TimeSpan.FromSeconds(10);

    internal static MessagePackRpcSerializer NewSerializer() => new();

    internal static Payload CreateRequestFrame(
        ISerializer serializer,
        int messageId,
        string service,
        string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest { MessageId = messageId, ServiceName = service, MethodName = method },
            ReadOnlySpan<byte>.Empty);
}
