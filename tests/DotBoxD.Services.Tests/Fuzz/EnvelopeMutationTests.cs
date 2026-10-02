using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Fuzz;

public sealed class EnvelopeMutationTests
{
    private static readonly MessagePackRpcSerializer Serializer = new();
    private static readonly string[] RequestFields = ["MessageId", "ServiceName", "MethodName", "InstanceId", "Streams"];
    private static readonly string[] ResponseFields = ["MessageId", "IsSuccess", "ErrorMessage", "ErrorType", "Stream"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_known_field_rejects_duplicates_in_every_position(bool response)
    {
        var fields = response ? ResponseFields : RequestFields;
        foreach (var field in fields)
        {
            for (var position = 0; position <= fields.Length; position++)
            {
                var mutated = fields.ToList();
                mutated.Insert(position, field);
                var bytes = WriteEnvelope(mutated, response);
                AssertRejected(bytes, response, $"duplicate {field}");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_or_misspelling_required_fields_never_uses_defaults(bool response)
    {
        var fields = response ? ResponseFields : RequestFields;
        var required = response ? new[] { "MessageId", "IsSuccess" } : ["MessageId", "ServiceName", "MethodName"];
        foreach (var field in required)
        {
            AssertRejected(WriteEnvelope(fields.Where(f => f != field), response), response, field);
            // Same-length lookalikes exercise optimized field dispatch, including every character.
            for (var character = 0; character < field.Length; character++)
            {
                var chars = field.ToCharArray();
                chars[character] = '#';
                var replacement = new string(chars);
                AssertRejected(WriteEnvelope(fields.Select(f => f == field ? replacement : f), response), response, field);
            }
        }
    }

    [Theory]
    [InlineData(false, 1729)]
    [InlineData(false, 104729)]
    [InlineData(true, 1729)]
    [InlineData(true, 104729)]
    public void Permuted_fields_and_nested_unknown_values_preserve_envelopes(bool response, int seed)
    {
        var random = new Random(seed);
        var fields = response ? ResponseFields : RequestFields;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var shuffled = fields.Append("FutureField").ToArray();
            random.Shuffle(shuffled);
            var bytes = WriteEnvelope(shuffled, response);
            if (response)
            {
                AssertResponse(Serializer.Deserialize<RpcResponse>(bytes));
                AssertResponse(Assert.IsType<RpcResponse>(Serializer.Deserialize(bytes, typeof(RpcResponse))));
            }
            else
            {
                AssertRequest(Serializer.Deserialize<RpcRequest>(bytes));
                AssertRequest(Assert.IsType<RpcRequest>(Serializer.Deserialize(bytes, typeof(RpcRequest))));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_truncation_and_trailing_byte_is_rejected(bool response)
    {
        var bytes = WriteEnvelope(response ? ResponseFields : RequestFields, response);
        for (var length = 0; length < bytes.Length; length++)
        {
            AssertRejected(bytes[..length], response);
        }

        for (var suffix = 0; suffix <= byte.MaxValue; suffix++)
        {
            AssertRejected([.. bytes, (byte)suffix], response, "Trailing bytes");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wrong_field_types_are_rejected(bool response)
    {
        foreach (var field in response ? ResponseFields : RequestFields)
        {
            // A map is invalid for every envelope field, including nullable strings and streams.
            AssertRejected(WriteEnvelope(response ? ResponseFields : RequestFields, response, field), response);
        }
    }

    private static byte[] WriteEnvelope(IEnumerable<string> fields, bool response, string? wrongType = null)
    {
        var names = fields.ToArray();
        var output = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(output);
        writer.WriteMapHeader(names.Length);
        foreach (var field in names)
        {
            writer.Write(field);
            if (field == wrongType)
            {
                writer.WriteMapHeader(0);
                continue;
            }

            switch (field)
            {
                case "MessageId":
                    writer.Write(int.MinValue);
                    break;
                case "ServiceName":
                    writer.Write("Service.世界");
                    break;
                case "MethodName":
                    writer.Write("Call🚀");
                    break;
                case "InstanceId":
                    writer.Write("instance\0id");
                    break;
                case "IsSuccess":
                    writer.Write(false);
                    break;
                case "ErrorMessage":
                    writer.Write("failure🚀");
                    break;
                case "ErrorType":
                    writer.Write("Error.Type");
                    break;
                case "Stream" or "Streams":
                    writer.WriteNil();
                    break;
                default:
                    writer.WriteMapHeader(1);
                    writer.Write("nested");
                    writer.WriteArrayHeader(3);
                    writer.Write(42);
                    writer.WriteNil();
                    writer.Write(response);
                    break;
            }
        }

        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    private static void AssertRejected(byte[] bytes, bool response, string? message = null)
    {
        var generic = Assert.Throws<MessagePackSerializationException>(() =>
        {
            if (response)
            { Serializer.Deserialize<RpcResponse>(bytes); }
            else
            { Serializer.Deserialize<RpcRequest>(bytes); }
        });
        var runtime = Assert.Throws<MessagePackSerializationException>(() =>
            Serializer.Deserialize(bytes, response ? typeof(RpcResponse) : typeof(RpcRequest)));
        if (message is not null)
        {
            Assert.Contains(message, generic.Message, StringComparison.Ordinal);
            Assert.Contains(message, runtime.Message, StringComparison.Ordinal);
        }
    }

    private static void AssertRequest(RpcRequest value)
    {
        Assert.Equal(int.MinValue, value.MessageId);
        Assert.Equal("Service.世界", value.ServiceName);
        Assert.Equal("Call🚀", value.MethodName);
        Assert.Equal("instance\0id", value.InstanceId);
        Assert.Null(value.Streams);
    }

    private static void AssertResponse(RpcResponse value)
    {
        Assert.Equal(int.MinValue, value.MessageId);
        Assert.False(value.IsSuccess);
        Assert.Equal("failure🚀", value.ErrorMessage);
        Assert.Equal("Error.Type", value.ErrorType);
        Assert.Null(value.Stream);
    }
}
