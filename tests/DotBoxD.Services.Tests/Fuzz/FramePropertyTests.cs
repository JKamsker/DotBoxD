using System.Buffers;
using System.Buffers.Binary;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Fuzz;

public sealed class FramePropertyTests
{
    [Theory]
    [InlineData(1729)]
    [InlineData(65537)]
    [InlineData(104729)]
    public void Structured_frames_match_wire_oracle_after_header_mutation(int seed)
    {
        var random = new Random(seed);
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var bytes = new byte[random.Next(13, 513)];
            random.NextBytes(bytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes, bytes.Length);
            bytes[8] = (byte)random.Next(1, 10);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(9), random.Next(bytes.Length - 12));
            AssertMatchesOracle(bytes);

            // Keep most of each valid frame intact so mutations reach deep parser branches.
            switch (iteration % 4)
            {
                case 0:
                    BinaryPrimitives.WriteInt32LittleEndian(bytes, random.Next(-32, 1024));
                    break;
                case 1:
                    bytes[8] = (byte)random.Next(256);
                    break;
                case 2:
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(9), random.Next(-32, 1024));
                    break;
                default:
                    Array.Resize(ref bytes, random.Next(bytes.Length));
                    break;
            }

            AssertMatchesOracle(bytes);
        }
    }

    [Fact]
    public void Every_message_type_byte_obeys_the_wire_contract()
    {
        for (var value = 0; value <= byte.MaxValue; value++)
        {
            var writer = new ArrayBufferWriter<byte>();
            var type = (MessageType)value;
            if (value is >= 1 and <= 9)
            {
                MessageFramer.WriteFrame(writer, int.MinValue, type, []);
                using var rented = MessageFramer.FrameToPayload(int.MinValue, type, []);
                Assert.Equal(writer.WrittenSpan.ToArray(), rented.Memory.ToArray());
                Assert.True(MessageFramer.TryReadFrameHeader(writer.WrittenMemory, out var id, out var actual));
                Assert.Equal(int.MinValue, id);
                Assert.Equal(type, actual);
            }
            else
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => MessageFramer.WriteFrame(writer, 0, type, []));
                Assert.Throws<ArgumentOutOfRangeException>(() => MessageFramer.FrameToPayload(0, type, []));
                Assert.Equal(0, writer.WrittenCount);
            }

            var frame = new byte[13];
            BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
            frame[8] = (byte)value;
            AssertMatchesOracle(frame);
        }
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(16777216)]
    [InlineData(16777217)]
    [InlineData(int.MaxValue)]
    public void Length_boundaries_and_overflow_are_checked(int length)
    {
        var frame = new byte[13];
        frame[8] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);
        AssertMatchesOracle(frame);
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(9), length);
        AssertMatchesOracle(frame);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    public void Maximum_frame_size_is_inclusive(int excess)
    {
        var frame = new byte[MessageFramer.MaxMessageSize + excess];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
        frame[8] = 1;
        Assert.Equal(excess == 0, MessageFramer.TryReadFrameHeader(frame, out _, out _));
        Assert.Equal(excess == 0, MessageFramer.TryReadFrame(frame, out _, out _, out _, out _));
    }

    private static void AssertMatchesOracle(byte[] bytes)
    {
        // Literal wire offsets and numeric tags deliberately do not reuse parser helpers.
        var headerValid = bytes.Length >= 9 && bytes.Length <= 16 * 1024 * 1024
            && BinaryPrimitives.ReadInt32LittleEndian(bytes) == bytes.Length
            && bytes[8] is >= 1 and <= 9;
        Assert.Equal(headerValid, MessageFramer.TryReadFrameHeader(bytes, out var headerId, out var headerType));
        if (headerValid)
        {
            Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), headerId);
            Assert.Equal(bytes[8], (byte)headerType);
        }

        var envelopeLength = bytes.Length >= 13 ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(9)) : -1;
        var frameValid = headerValid && bytes.Length >= 13 && envelopeLength >= 0 && envelopeLength <= bytes.Length - 13;
        Assert.Equal(frameValid, MessageFramer.TryReadFrame(bytes, out var id, out var type, out var envelope, out var payload));
        if (frameValid)
        {
            Assert.Equal(headerId, id);
            Assert.Equal(headerType, type);
            Assert.Equal(bytes.AsSpan(13, envelopeLength).ToArray(), envelope.ToArray());
            Assert.Equal(bytes.AsSpan(13 + envelopeLength).ToArray(), payload.ToArray());
        }
        else
        {
            Assert.True(envelope.IsEmpty);
            Assert.True(payload.IsEmpty);
        }
    }
}
