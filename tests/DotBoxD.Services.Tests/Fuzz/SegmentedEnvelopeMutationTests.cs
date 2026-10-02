using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Fuzz;

public sealed class SegmentedEnvelopeMutationTests
{
    private static readonly MessagePackSerializerOptions Options = new MessagePackRpcSerializer().Options;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void All_field_dispatch_branches_work_across_single_byte_segments(bool response)
    {
        var fields = response
            ? new[] { "MessageId", "IsSuccess", "ErrorMessage", "ErrorType", "Stream" }
            : ["MessageId", "ServiceName", "MethodName", "InstanceId", "Streams"];
        var bytes = Envelope(response, fields.Concat([null, "x", "Unknown", "Unknown000", "M#########", "I########", "E########", "StreamsX"]));
        if (response)
        {
            var value = Read<RpcResponse>(bytes);
            Assert.Equal(42, value.MessageId);
            Assert.False(value.IsSuccess);
            Assert.Equal("failure", value.ErrorMessage);
            Assert.Equal("Error", value.ErrorType);
            Assert.Null(value.Stream);
        }
        else
        {
            var value = Read<RpcRequest>(bytes);
            Assert.Equal(42, value.MessageId);
            Assert.Equal("Service", value.ServiceName);
            Assert.Equal("Call", value.MethodName);
            Assert.Equal("instance", value.InstanceId);
            Assert.Null(value.Streams);
        }

        foreach (var field in fields)
        {
            var duplicated = Envelope(response, fields.Append(field));
            Assert.Contains($"duplicate {field}", Reject(duplicated, response).ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Segmented_same_length_lookalikes_are_not_recognized_fields(bool response)
    {
        var fields = response ? new[] { "MessageId", "IsSuccess", "ErrorMessage", "ErrorType" } : ["MessageId", "ServiceName", "MethodName"];
        foreach (var field in fields)
        {
            for (var index = 0; index < field.Length; index++)
            {
                var lookalike = field[..index] + "#" + field[(index + 1)..];
                var bytes = Envelope(response, fields.Select(f => f == field ? lookalike : f));
                Assert.Contains(field, Reject(bytes, response).ToString(), StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_field_depth_errors_identify_the_envelope(bool response)
    {
        var fields = response ? new[] { "MessageId", "IsSuccess", "ErrorMessage", "ErrorType", "deep" } : ["MessageId", "ServiceName", "MethodName", "deep"];
        var exception = Reject(Envelope(response, fields), response);
        Assert.Contains(response ? "RPC response contains an unknown field" : "RPC request contains an unknown field",
            exception.ToString(), StringComparison.Ordinal);
    }

    private static byte[] Envelope(bool response, IEnumerable<string?> fields)
    {
        var names = fields.ToArray();
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(names.Length);
        foreach (var field in names)
        {
            writer.Write(field);
            switch (field)
            {
                case "MessageId":
                    writer.Write(42);
                    break;
                case "ServiceName":
                    writer.Write("Service");
                    break;
                case "MethodName":
                    writer.Write("Call");
                    break;
                case "InstanceId":
                    writer.Write("instance");
                    break;
                case "IsSuccess":
                    writer.Write(false);
                    break;
                case "ErrorMessage":
                    writer.Write("failure");
                    break;
                case "ErrorType":
                    writer.Write("Error");
                    break;
                case "deep":
                    for (var i = 0; i < 65; i++)
                    {
                        writer.WriteArrayHeader(1);
                    }
                    writer.Write(response);
                    break;
                default:
                    writer.WriteNil();
                    break;
            }
        }

        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static MessagePackSerializationException Reject(byte[] bytes, bool response)
        => Assert.Throws<MessagePackSerializationException>(() =>
        {
            if (response)
            {
                Read<RpcResponse>(bytes);
            }
            else
            {
                Read<RpcRequest>(bytes);
            }
        });

    private static T Read<T>(byte[] bytes)
    {
        var first = new Segment(bytes.AsMemory(0, 1));
        var last = first;
        for (var i = 1; i < bytes.Length; i++)
        {
            last = last.Append(bytes.AsMemory(i, 1));
        }
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(first, 0, last, 1));
        var value = MessagePackSerializer.Deserialize<T>(ref reader, Options);
        Assert.True(reader.End);
        return value;
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}
