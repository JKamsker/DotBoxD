using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Fuzz;

public sealed class EnvelopeDepthMutationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Request_stream_array_restores_reader_depth_on_success_and_failure(bool malformed)
    {
        var options = new MessagePackRpcSerializer().Options;
        var bytes = Request(malformed);
        var reader = new MessagePackReader(bytes) { Depth = 7 };
        var formatter = options.Resolver.GetFormatterWithVerify<RpcRequest>();
        if (malformed)
        {
            try
            {
                formatter.Deserialize(ref reader, options);
                Assert.Fail("Malformed stream must be rejected.");
            }
            catch (MessagePackSerializationException)
            {
                Assert.Equal(7, reader.Depth);
            }
        }
        else
        {
            var request = formatter.Deserialize(ref reader, options);
            Assert.Empty(Assert.IsType<RpcStreamHandle[]>(request.Streams));
            Assert.Equal(7, reader.Depth);
        }
    }

    [Fact]
    public void Request_stream_array_respects_the_configured_depth_limit()
    {
        var serializer = new MessagePackRpcSerializer();
        var options = serializer.Options.WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(1));
        var reader = new MessagePackReader(Request(malformed: false)) { Depth = 1 };
        try
        {
            options.Resolver.GetFormatterWithVerify<RpcRequest>().Deserialize(ref reader, options);
            Assert.Fail("The stream array must consume a depth level.");
        }
        catch (InsufficientExecutionStackException)
        {
            Assert.Equal(1, reader.Depth);
        }
    }

    private static byte[] Request(bool malformed)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(output);
        writer.WriteMapHeader(4);
        writer.Write("MessageId");
        writer.Write(1);
        writer.Write("ServiceName");
        writer.Write("Service");
        writer.Write("MethodName");
        writer.Write("Call");
        writer.Write("Streams");
        if (malformed)
        {
            writer.Write(true);
        }
        else
        {
            writer.WriteArrayHeader(0);
        }
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }
}
