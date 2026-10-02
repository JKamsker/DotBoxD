using System.Buffers;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Fuzz;

public sealed class StreamFramePropertyTests
{
    [Theory]
    [InlineData(1729)]
    [InlineData(65537)]
    [InlineData(104729)]
    public async Task Fragmented_concatenated_frames_preserve_boundaries_and_payloads(int seed)
    {
        var random = new Random(seed);
        var frames = new List<(int Id, MessageType Type, byte[] Body)>();
        var writer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < 100; i++)
        {
            var body = new byte[random.Next(1025)];
            random.NextBytes(body);
            var id = (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
            var type = (MessageType)random.Next(1, 10);
            frames.Add((id, type, body));
            MessageFramer.WriteFrame(writer, id, type, body);
        }

        using var stream = new FragmentedStream(writer.WrittenSpan.ToArray(), random);
        foreach (var expected in frames)
        {
            var actual = await MessageFramer.ReadMessageAsync(stream);
            Assert.NotNull(actual);
            using var body = actual.Value.Body;
            Assert.Equal(expected.Id, actual.Value.MessageId);
            Assert.Equal(expected.Type, actual.Value.Type);
            Assert.Equal(expected.Body, body.Memory.ToArray());
        }

        Assert.Null(await MessageFramer.ReadMessageAsync(stream));
    }

    [Fact]
    public async Task Every_truncation_is_rejected_except_clean_end_of_stream()
    {
        var writer = new ArrayBufferWriter<byte>();
        MessageFramer.WriteFrame(writer, -123, MessageType.StreamItem, Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());
        for (var length = 0; length < writer.WrittenCount; length++)
        {
            using var stream = new FragmentedStream(writer.WrittenSpan[..length].ToArray(), new Random(length));
            if (length == 0)
            {
                Assert.Null(await MessageFramer.ReadMessageAsync(stream));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => MessageFramer.ReadMessageAsync(stream));
            }
        }
    }

    private sealed class FragmentedStream(byte[] bytes, Random random) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, random.Next(1, 8))], cancellationToken);
    }
}
