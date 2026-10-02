using System.Buffers;
using System.Text;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Fuzz;

public sealed class UnicodePropertyTests
{
    [Theory]
    [InlineData(1729)]
    [InlineData(65537)]
    [InlineData(104729)]
    public void Unicode_scalars_roundtrip_and_unpaired_surrogates_are_rejected(int seed)
    {
        var serializer = new MessagePackRpcSerializer();
        var random = new Random(seed);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var text = new StringBuilder();
            for (var i = 0; i < random.Next(1, 32); i++)
            {
                var scalar = random.Next(0x110000);
                if (scalar is >= 0xD800 and <= 0xDFFF)
                { scalar = 0; }
                text.Append(char.ConvertFromUtf32(scalar));
            }

            var valid = text.ToString();
            var output = new ArrayBufferWriter<byte>();
            serializer.Serialize(output, valid);
            Assert.Equal(valid, serializer.Deserialize<string>(output.WrittenMemory));
            Assert.Equal(valid, serializer.Deserialize(output.WrittenMemory, typeof(string)));

            var surrogate = ((char)random.Next(0xD800, 0xE000)).ToString();
            // ASCII separators guarantee the inserted surrogate cannot pair with generated text.
            foreach (var invalid in new[] { surrogate + "a" + valid, valid + "a" + surrogate, "a" + surrogate + "a" })
            {
                Assert.Throws<MessagePackSerializationException>(() => serializer.Serialize(new ArrayBufferWriter<byte>(), invalid));
            }
        }
    }

    [Fact]
    public void String_null_empty_and_surrogate_boundaries_remain_distinct()
    {
        var serializer = new MessagePackRpcSerializer();
        foreach (var value in new[] { null, "", "\0", "\uD7FF", "\uE000", "\uFFFF", "\U00010000", "\U0010FFFF", "a🚀b🚀c" })
        {
            var output = new ArrayBufferWriter<byte>();
            serializer.Serialize(output, value);
            Assert.Equal(value, serializer.Deserialize<string?>(output.WrittenMemory));
        }
    }
}
