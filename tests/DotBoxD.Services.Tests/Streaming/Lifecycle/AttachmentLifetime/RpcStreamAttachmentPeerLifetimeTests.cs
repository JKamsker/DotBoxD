using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Streaming.Remote;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.AttachmentLifetime;

public sealed class RpcStreamAttachmentPeerLifetimeTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Completed_peer_call_releases_owned_attachment_source(bool pipe, bool owned, bool retain)
    {
        var fixture = await ExercisePeers(pipe, owned, retain);
        if (owned || !retain)
        {
            await fixture.AssertCollected();
        }
        else
        {
            AttachmentLifetimeFixture.Collect();
            Assert.True(fixture.Source.IsAlive);
        }

        GC.KeepAlive(fixture);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<AttachmentLifetimeFixture> ExercisePeers(bool usePipe, bool owned, bool retain)
    {
        var (left, right) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        await using var server = RpcPeer.Over(right, serializer).Provide(new Receiver()).Start();
        await using var client = RpcPeer.Over(left, serializer).Start();
        var handle = client.ReserveStream(RpcStreamKind.Binary);
        RpcStreamAttachment attachment;
        WeakReference source;
        if (usePipe)
        {
            var pipe = new Pipe();
            pipe.Writer.Write(new byte[] { 42 });
            await pipe.Writer.CompleteAsync();
            source = new WeakReference(pipe);
            attachment = RpcStreamAttachment.FromPipe(handle, pipe, completeReader: owned);
        }
        else
        {
            var stream = new MemoryStream(new byte[] { 42 });
            source = new WeakReference(stream);
            attachment = RpcStreamAttachment.FromStream(handle, stream, leaveOpen: !owned);
        }

        var result = await client.InvokeAsync<RpcStreamHandle, int>("Receiver", "Read", handle, attachment)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, result);
        return new AttachmentLifetimeFixture(retain ? attachment : null, source, new AttachmentLifetimeState());
    }

    private sealed class Receiver : IServiceDispatcher
    {
        public string ServiceName => "Receiver";

        public Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
            IInstanceRegistry registry, IBufferWriter<byte> output, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
            IInstanceRegistry registry, IBufferWriter<byte> output, IRpcStreamingContext streaming, CancellationToken ct = default)
        {
            var handle = serializer.Deserialize<RpcStreamHandle>(payload);
            await using var stream = streaming.GetStream(handle);
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes, ct);
            serializer.Serialize(output, (int)Assert.Single(bytes.ToArray()));
        }
    }
}
