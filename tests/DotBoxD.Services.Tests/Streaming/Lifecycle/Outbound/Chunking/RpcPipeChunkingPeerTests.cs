using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Streaming.Remote;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Chunking;

public sealed class RpcPipeChunkingPeerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Binary_sources_larger_than_one_frame_transfer_identical_bytes(bool usePipe)
    {
        var payload = new byte[MessageFramer.MaxMessageSize + 1];
        new Random(42).NextBytes(payload);
        var expected = Convert.ToHexString(SHA256.HashData(payload));
        var (left, right) = InMemoryPipe.CreateConnectionPair();
        var channel = new TrackingChannel(left);
        var serializer = new MessagePackRpcSerializer();
        await using var server = RpcPeer.Over(right, serializer).Provide(new HashDispatcher()).Start();
        await using var client = RpcPeer.Over(channel, serializer,
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) }).Start();
        var handle = client.ReserveStream(RpcStreamKind.Binary);
        Pipe? pipe = null;
        var producer = Task.CompletedTask;
        RpcStreamAttachment attachment;
        if (usePipe)
        {
            pipe = new Pipe();
            producer = Produce(pipe, payload);
            Assert.False(producer.IsCompleted);
            attachment = RpcStreamAttachment.FromPipe(handle, pipe, completeReader: true);
        }
        else
        {
            attachment = RpcStreamAttachment.FromStream(handle, new MemoryStream(payload, writable: false), leaveOpen: false);
        }

        try
        {
            var actual = await client.InvokeAsync<RpcStreamHandle, string>("ChunkHash", "Hash", handle, attachment);

            Assert.Equal(expected, actual);
            Assert.Equal(payload.Length, channel.BytesSent);
            Assert.Equal(257, channel.Frames);
            Assert.InRange(channel.LargestPayload, 1, PipeChunkingFixture.ChunkSize);
            await producer.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (pipe is not null)
            {
                await pipe.Reader.CompleteAsync();
            }

            await producer.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task Produce(Pipe pipe, byte[] payload)
    {
        payload.AsMemory().CopyTo(pipe.Writer.GetMemory(payload.Length));
        pipe.Writer.Advance(payload.Length);
        await pipe.Writer.FlushAsync();
        await pipe.Writer.CompleteAsync();
    }

    private sealed class TrackingChannel(IRpcChannel inner) : IRpcChannel
    {
        public int BytesSent { get; private set; }
        public int Frames { get; private set; }
        public int LargestPayload { get; private set; }
        public bool IsConnected => inner.IsConnected;
        public string RemoteEndpoint => inner.RemoteEndpoint;
        public Task<Payload> ReceiveAsync(CancellationToken ct = default) => inner.ReceiveAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (MessageFramer.TryReadFrameHeader(data, out _, out var type) && type == MessageType.StreamItem)
            {
                var length = data.Length - MessageFramer.HeaderSize;
                BytesSent += length;
                Frames++;
                LargestPayload = Math.Max(LargestPayload, length);
            }

            return inner.SendAsync(data, ct);
        }
    }

    private sealed class HashDispatcher : IServiceDispatcher
    {
        public string ServiceName => "ChunkHash";

        public Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
            IInstanceRegistry registry, IBufferWriter<byte> output, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
            IInstanceRegistry registry, IBufferWriter<byte> output, IRpcStreamingContext streaming, CancellationToken ct = default)
        {
            await using var source = streaming.GetStream(serializer.Deserialize<RpcStreamHandle>(payload));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[PipeChunkingFixture.ChunkSize];
            int count;
            while ((count = await source.ReadAsync(buffer, ct)) != 0)
            {
                hash.AppendData(buffer, 0, count);
            }

            serializer.Serialize(output, Convert.ToHexString(hash.GetHashAndReset()));
        }
    }
}
