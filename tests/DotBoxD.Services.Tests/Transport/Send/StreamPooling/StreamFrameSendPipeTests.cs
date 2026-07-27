using System.IO.Pipes;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.StreamPooling;

[Collection(StreamSendOperationCollection.Name)]
public sealed class StreamFrameSendPipeTests
{
    [Fact]
    public async Task PipeStreamSend_SkipsFlush()
    {
        await using var stream = new CountingPipeStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Create(90, out var expected);

        var send = connection.SendFrameValueAsync(frame);

        Assert.True(send.IsCompletedSuccessfully);
        await send;
        Assert.Equal(expected, stream.WrittenBytes);
        Assert.Equal(1, stream.WriteCount);
        Assert.Equal(0, stream.FlushCount);
        StreamSendTestFrames.AssertDisposed(frame);
    }

    private sealed class CountingPipeStream : PipeStream
    {
        public CountingPipeStream()
            : base(PipeDirection.Out, bufferSize: 1)
        {
        }

        public int FlushCount { get; private set; }
        public int WriteCount { get; private set; }
        public byte[]? WrittenBytes { get; private set; }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            WrittenBytes = buffer.ToArray();
            return default;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FlushCount++;
            return Task.CompletedTask;
        }
    }
}
