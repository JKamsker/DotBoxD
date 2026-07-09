using System.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Transport;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Peer.PeerOutboundTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed class PeerOutboundMaterializationCancellationTests
{
    [Fact]
    public async Task InvokeAsync_WhenResponseDeserializerCancelsCallerToken_ThrowsCancellation()
    {
        using var cts = new CancellationTokenSource();
        var serializer = new ResponseCancellingSerializer(NewSerializer(), cts);
        await using var channel = new RecordingChannel();
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1, cts.Token);
        var messageIds = await channel.WaitForSentFrameIdsAsync(1, PeerOutboundTimeout);

        channel.Enqueue(ResponseFrame(serializer, messageIds[0], result: "decoded"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(PeerOutboundTimeout));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task PendingUnaryResponse_WhenResponseDeserializerCancelsCallerToken_CompletesTaskAsCanceled()
    {
        using var owner = new PendingRequests();
        using var cts = new CancellationTokenSource();
        var serializer = new ResponseCancellingSerializer(NewSerializer(), cts);
        Assert.True(owner.TryAddUnary<string>(
            messageId: 42,
            captureCallerCancellation: true,
            captureTimeoutTarget: false,
            cts.Token,
            Service,
            Method,
            out var pending));

        using var framePayload = ResponseFrame(serializer, messageId: 42, result: "decoded");
        Assert.True(MessageFramer.TryReadFrame(
            framePayload.Memory,
            out _,
            out _,
            out var envelope,
            out var payload));
        var response = serializer.Deserialize<RpcResponse>(envelope);

        Assert.True(pending.TrySetResponse(response, payload, new RpcFrame(framePayload), stream: null, serializer));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Task.WaitAsync(PeerOutboundTimeout));
        Assert.True(pending.Task.IsCanceled);
    }

    [Fact]
    public void PendingUnaryResponse_LateCancellationDoesNotOverwriteWinningKind()
    {
        using var owner = new PendingRequests();
        using var cts = new CancellationTokenSource();
        Assert.True(owner.TryAddUnary<string>(
            messageId: 43,
            captureCallerCancellation: true,
            captureTimeoutTarget: false,
            cts.Token,
            Service,
            Method,
            out var pending));

        Assert.True(owner.TryCancel(43, pending, PendingCancellationKind.Timeout));

        pending.TrySetCanceled(PendingCancellationKind.Caller);

        Assert.Equal(PendingCancellationKind.Timeout, pending.CancellationKind);
    }

    [Fact]
    public void PendingReceivedResponse_LateCancellationDoesNotOverwriteWinningKind()
    {
        using var owner = new PendingRequests();
        Assert.True(owner.TryAdd(messageId: 44, out var pending));

        Assert.True(owner.TryCancel(44, pending, PendingCancellationKind.Timeout));

        pending.TrySetCanceled(PendingCancellationKind.Caller);

        Assert.Equal(PendingCancellationKind.Timeout, pending.CancellationKind);
    }

    private sealed class ResponseCancellingSerializer : ISerializer
    {
        private readonly ISerializer _inner;
        private readonly CancellationTokenSource _cts;

        public ResponseCancellingSerializer(ISerializer inner, CancellationTokenSource cts)
        {
            _inner = inner;
            _cts = cts;
        }

        public void Serialize<T>(IBufferWriter<byte> writer, T value) =>
            _inner.Serialize(writer, value);

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            if (typeof(T) == typeof(string))
            {
                _cts.Cancel();
            }

            return _inner.Deserialize<T>(data);
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
        {
            if (type == typeof(string))
            {
                _cts.Cancel();
            }

            return _inner.Deserialize(data, type);
        }
    }
}
