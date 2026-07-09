using System.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Serialization;
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

        await Task.Delay(25);
        channel.Enqueue(ResponseFrame(serializer, messageIds[0], result: "decoded"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(PeerOutboundTimeout));
        Assert.True(cts.IsCancellationRequested);
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
