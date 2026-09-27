using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Chunks;

internal sealed class ChunkDisposalFixture : IDisposable
{
    public required RpcStreamManager Manager { get; init; }
    public required RpcStreamReceiver Receiver { get; init; }
    public required RpcStreamChunk Chunk { get; init; }
    public required Payload Frame { get; init; }
    public required CreditSender Sender { get; init; }
    public Task<RpcStreamChunk?>? Read { get; init; }

    public static ChunkDisposalFixture Create(bool pendingRead = false, Task? pendingCredit = null)
    {
        var sender = new CreditSender(pendingCredit ?? Task.CompletedTask);
        var manager = new RpcStreamManager(new MessagePackRpcSerializer(), sender.SendAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(101, RpcStreamKind.Binary);
        var receiver = manager.RegisterInboundResponse(handle, CancellationToken.None);
        Assert.Equal(RpcStreamManager.WindowSize, sender.Credits);
        sender.Credits = 0;
        var read = pendingRead ? receiver.ReadChunkAsync(CancellationToken.None).AsTask() : null;
        if (read is not null)
        {
            Assert.False(read.IsCompleted);
        }
        var frame = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 42 });
        Assert.True(manager.TryAcceptItem(handle.StreamId, frame));
        var chunk = read is null
            ? receiver.ReadChunkAsync(CancellationToken.None).GetAwaiter().GetResult()
            : read.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        Assert.NotNull(chunk);
        Assert.Equal(new byte[] { 42 }, chunk.Payload.ToArray());
        return new ChunkDisposalFixture
        {
            Manager = manager,
            Receiver = receiver,
            Chunk = chunk,
            Frame = frame,
            Sender = sender,
            Read = read,
        };
    }

    public WeakReference[] OwnershipReferences()
        => [new(Receiver), new(Manager), new(Sender)];

    public void Dispose()
    {
        Chunk.DisposeWithoutCredit();
        Manager.Stop();
    }

    internal sealed class CreditSender(Task pendingCredit)
    {
        public int Credits;
        public bool InvalidFrame { get; private set; }

        public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MessageFramer.TryReadFrameHeader(frame, out _, out var type) && type == MessageType.StreamCredit)
            {
                if (!RpcRawFrame.TryReadInt32(frame, out var count))
                {
                    InvalidFrame = true;
                    return Task.CompletedTask;
                }
                Interlocked.Add(ref Credits, count);
                if (count == 1)
                {
                    return pendingCredit;
                }
            }
            return Task.CompletedTask;
        }
    }
}
