using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Tests.Support;
using Xunit;
using Shared;

namespace DotBoxD.Services.Tests.Coverage.RoundsLate;

public sealed class NoPayloadProtocolSurpriseTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Generated_no_request_dispatch_rejects_unexpected_payload_bytes()
    {
        var serializer = new MessagePackRpcSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .Provide<IGameService>(new TestGameService())
            .Start();

        using var requestFrame = BuildRequestFrameWithPayload(
            serializer,
            messageId: 1,
            service: "IGameService",
            method: "GetServerStatusAsync");
        await client.SendAsync(requestFrame.Memory);

        using var responseFrame = await client.ReceiveAsync().WaitAsync(Timeout);

        Assert.True(MessageFramer.TryReadFrame(
            responseFrame.Memory,
            out var messageId,
            out var messageType,
            out var envelope,
            out _));
        var response = serializer.Deserialize<RpcResponse>(envelope);

        Assert.Equal(1, messageId);
        Assert.Equal(MessageType.Error, messageType);
        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.ProtocolError, response.ErrorType);
        Assert.Contains("payload", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_no_response_rejects_unexpected_payload_bytes()
    {
        var serializer = new MessagePackRpcSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer).Start();

        var call = peer.InvokeAsync("Svc", "Op");
        channel.Enqueue(BuildResponseFrameWithPayload(serializer, messageId: 1));

        var ex = await Assert.ThrowsAsync<ServiceProtocolException>(() => call.WaitAsync(Timeout));
        Assert.Contains("payload", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeOnInstanceAsync_no_response_rejects_unexpected_payload_bytes()
    {
        var serializer = new MessagePackRpcSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, serializer).Start();

        var call = peer.InvokeOnInstanceAsync("Svc", "inst", "Op");
        channel.Enqueue(BuildResponseFrameWithPayload(serializer, messageId: 1));

        var ex = await Assert.ThrowsAsync<ServiceProtocolException>(() => call.WaitAsync(Timeout));
        Assert.Contains("payload", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeValueAsync_no_response_rejects_unexpected_payload_bytes()
    {
        var serializer = new MessagePackRpcSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer
            .Over(
                channel,
                serializer,
                new RpcPeerOptions
                {
                    EnableLowAllocationValueTaskInvocations = true,
                    RequestTimeout = System.Threading.Timeout.InfiniteTimeSpan,
                })
            .Start();

        var call = peer.InvokeValueAsync("Svc", "Op");
        channel.Enqueue(BuildResponseFrameWithPayload(serializer, messageId: 1));

        var ex = await Assert.ThrowsAsync<ServiceProtocolException>(
            () => call.AsTask().WaitAsync(Timeout));
        Assert.Contains("payload", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Payload BuildRequestFrameWithPayload(
        MessagePackRpcSerializer serializer,
        int messageId,
        string service,
        string method)
    {
        var payload = SerializeGarbagePayload(serializer);
        return MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = messageId,
                ServiceName = service,
                MethodName = method,
            },
            payload.WrittenSpan);
    }

    private static Payload BuildResponseFrameWithPayload(
        MessagePackRpcSerializer serializer,
        int messageId)
    {
        var payload = SerializeGarbagePayload(serializer);
        return MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Response,
            new RpcResponse { MessageId = messageId, IsSuccess = true },
            payload.WrittenSpan);
    }

    private static ArrayBufferWriter<byte> SerializeGarbagePayload(MessagePackRpcSerializer serializer)
    {
        var payload = new ArrayBufferWriter<byte>();
        serializer.Serialize(payload, "unexpected");
        return payload;
    }
}
