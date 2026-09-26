using System.Buffers;
using System.IO.Pipelines;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound;

public sealed class RpcPipeAttachmentCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledRead_PreservesBufferedBytesInBorrowedPipe(bool writerCompleted)
    {
        var pipe = new Pipe();
        var serializer = new MessagePackRpcSerializer();
        var sentItems = 0;
        var streams = new RpcStreamManager(serializer, SendAsync, exceptionTransformer: null);
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromPipe(handle, pipe, completeReader: false);
        await using var outbound = streams.RegisterOutbound(attachment, CancellationToken.None);
        var payload = new byte[] { 1, 2, 3, 4 };
        await pipe.Writer.WriteAsync(payload);
        if (writerCompleted)
        {
            await pipe.Writer.CompleteAsync();
        }
        pipe.Reader.CancelPendingRead();

        try
        {
            await attachment.PumpCoreAsync(streams, serializer, CancellationToken.None).WaitAsync(Timeout);
            if (!writerCompleted)
            {
                await pipe.Writer.CompleteAsync();
            }

            var remaining = await pipe.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
            try
            {
                Assert.Equal(payload, remaining.Buffer.ToArray());
                Assert.Equal(0, sentItems);
            }
            finally
            {
                pipe.Reader.AdvanceTo(remaining.Buffer.End);
            }
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
            await pipe.Writer.CompleteAsync();
        }

        Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            if (MessageFramer.TryReadFrameHeader(frame, out _, out var type) && type == MessageType.StreamItem)
            {
                sentItems++;
            }
            return Task.CompletedTask;
        }
    }

}
