using System.Buffers;
using System.IO.Pipelines;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Streaming.Remote;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Progress;

public sealed class RpcPipeSendProgressPeerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Failed_peer_upload_preserves_unsent_bytes(int failAt)
    {
        var (left, right) = InMemoryPipe.CreateConnectionPair();
        var receiver = new Receiver();
        var channel = new FailingItemChannel(left, failAt);
        var serializer = new MessagePackRpcSerializer();
        await using var server = RpcPeer.Over(right, serializer).Provide(receiver).Start();
        await using var client = RpcPeer.Over(channel, serializer, new RpcPeerOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(5),
            ExceptionTransformer = static error => error is IOException ? new RpcErrorInfo("Upload send failed", "UploadSendFailure") : null,
        }).Start();
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 4096, pauseWriterThreshold: 0, resumeWriterThreshold: 0));
        var payloads = new List<byte[]>();
        for (var i = 0; i < 2; i++)
        {
            var memory = pipe.Writer.GetMemory(4096);
            memory.Span.Fill((byte)(i + 1));
            payloads.Add(memory.ToArray());
            pipe.Writer.Advance(memory.Length);
        }

        await pipe.Writer.CompleteAsync();
        var handle = client.ReserveStream(RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromPipe(handle, pipe, completeReader: false);
        try
        {
            var error = await Assert.ThrowsAsync<RemoteServiceException>(() =>
                client.InvokeAsync<RpcStreamHandle>("PipeProgress", "Read", handle, attachment));
            Assert.Equal("UploadSendFailure", error.RemoteExceptionType);
            Assert.Equal(failAt, channel.Attempts);
            Assert.Equal(payloads.Take(failAt - 1).SelectMany(bytes => bytes), receiver.Received);

            var remaining = await pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                Assert.Equal(payloads.Skip(failAt - 1).SelectMany(bytes => bytes), remaining.Buffer.ToArray());
            }
            finally
            {
                pipe.Reader.AdvanceTo(remaining.Buffer.End);
            }
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
        }
    }

    private sealed class FailingItemChannel(IRpcChannel inner, int failAt) : IRpcChannel
    {
        public int Attempts { get; private set; }
        public bool IsConnected => inner.IsConnected;
        public string RemoteEndpoint => inner.RemoteEndpoint;
        public Task<Payload> ReceiveAsync(CancellationToken ct = default) => inner.ReceiveAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (MessageFramer.TryReadFrameHeader(data, out _, out var type) && type == MessageType.StreamItem && ++Attempts == failAt)
            {
                throw new IOException("Channel did not accept the segment.");
            }

            return inner.SendAsync(data, ct);
        }
    }

    private sealed class Receiver : IServiceDispatcher
    {
        public string ServiceName => "PipeProgress";
        public byte[] Received { get; private set; } = [];

        public Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
            IInstanceRegistry registry, IBufferWriter<byte> output, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
            IInstanceRegistry registry, IBufferWriter<byte> output, IRpcStreamingContext streaming, CancellationToken ct = default)
        {
            await using var source = streaming.GetStream(serializer.Deserialize<RpcStreamHandle>(payload));
            using var bytes = new MemoryStream();
            try
            {
                await source.CopyToAsync(bytes, ct);
            }
            finally
            {
                Received = bytes.ToArray();
            }
        }
    }
}
