using System.Buffers;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle;

/// <summary>
/// A locally canceled inbound stream leaves a tombstone until the remote sender acknowledges the
/// StreamCancel with a terminal frame. Without that acknowledgement, canceling more than
/// <see cref="RpcCanceledInboundStreams.Capacity"/> response streams on one connection exhausted
/// the tombstone tracker and every later inbound stream registration failed.
/// </summary>
public sealed class StreamingRemoteCancelTombstoneTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task BreakingOutOfInfiniteRemoteEnumerable_ManyTimes_KeepsStreamingUsable()
    {
        var service = new InfiniteNumbersDispatcher();
        await using var pair = await StreamingPeerPair.StartAsync(service);
        var protocolErrors = 0;
        pair.Client.ProtocolError += (_, _) => Interlocked.Increment(ref protocolErrors);
        pair.Server.ProtocolError += (_, _) => Interlocked.Increment(ref protocolErrors);

        for (var i = 0; i < RpcCanceledInboundStreams.Capacity + 76; i++)
        {
            var first = await TakeFirstAsync(pair).WaitAsync(TestTimeout);
            Assert.Equal(0, first);
        }

        var items = new List<int>();
        await foreach (var item in pair.Client.InvokeAsyncEnumerable<int>("Infinite", "Numbers"))
        {
            items.Add(item);
            if (items.Count == 3)
            {
                break;
            }
        }

        Assert.Equal(new[] { 0, 1, 2 }, items);
        Assert.Equal(0, Volatile.Read(ref protocolErrors));
    }

    [Fact]
    public async Task StreamCancelBeforeOutboundRegistration_AcknowledgesWithStreamComplete()
    {
        var frames = new FrameLog();
        var streams = new RpcStreamManager(new MessagePackRpcSerializer(), frames.SendAsync, exceptionTransformer: null);
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);

        streams.CancelOutbound(handle.StreamId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var outbound = streams.RegisterOutbound(
                RpcStreamAttachment.FromStream(handle, new MemoryStream()),
                CancellationToken.None);
        });

        Assert.Equal(
            (handle.StreamId, MessageType.StreamComplete),
            await frames.Next.WaitAsync(TestTimeout));
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    [Fact]
    public async Task RolledBackInboundRegistration_AsksSenderForTerminalFrame()
    {
        var frames = new FrameLog();
        var streams = new RpcStreamManager(new MessagePackRpcSerializer(), frames.SendAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(31_000, RpcStreamKind.Binary);

        Assert.Throws<ServiceProtocolException>(() =>
            streams.RegisterInbound(new[] { handle, handle }, CancellationToken.None));

        await frames.WaitForAsync(handle.StreamId, MessageType.StreamCancel).WaitAsync(TestTimeout);
        Assert.Equal(1, streams.CanceledInboundCount);
        Assert.True(streams.TryCompleteInbound(handle.StreamId));
        Assert.Equal(0, streams.CanceledInboundCount);
    }

    private static async Task<int> TakeFirstAsync(StreamingPeerPair pair)
    {
        await foreach (var item in pair.Client.InvokeAsyncEnumerable<int>("Infinite", "Numbers"))
        {
            return item;
        }

        throw new InvalidOperationException("The infinite stream completed.");
    }

    private sealed class FrameLog
    {
        private readonly System.Threading.Channels.Channel<(int, MessageType)> _frames =
            System.Threading.Channels.Channel.CreateUnbounded<(int, MessageType)>();

        public Task<(int, MessageType)> Next => _frames.Reader.ReadAsync().AsTask();

        public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out var id, out var type));
            _frames.Writer.TryWrite((id, type));
            return Task.CompletedTask;
        }

        public async Task WaitForAsync(int streamId, MessageType type)
        {
            while (await Next.ConfigureAwait(false) != (streamId, type))
            {
            }
        }
    }
}

internal sealed class InfiniteNumbersDispatcher : IServiceDispatcher
{
    public string ServiceName => "Infinite";

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Only the streaming dispatch overload is used.");

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        IRpcStreamingContext streaming,
        CancellationToken ct = default)
    {
        streaming.SetResponse(NumbersAsync(ct));
        return Task.CompletedTask;
    }

    private static async IAsyncEnumerable<int> NumbersAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; ; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }
}
