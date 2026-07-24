using System.Buffers;
using System.Text;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class RpcEnvelopeFieldDispatchTests
{
    [Fact]
    public void RpcRequest_EqualShapeNearMissFieldsRemainUnknown()
    {
        var serializer = new MessagePackRpcSerializer();

        var request = serializer.Deserialize<RpcRequest>(WriteRequestWithNearMissFields());

        Assert.Equal(73, request.MessageId);
        Assert.Equal("ExactService", request.ServiceName);
        Assert.Equal("ExactMethod", request.MethodName);
        Assert.Null(request.InstanceId);
        Assert.Null(request.Streams);
    }

    [Fact]
    public void RpcResponse_EqualShapeNearMissFieldsRemainUnknown()
    {
        var serializer = new MessagePackRpcSerializer();

        var response = serializer.Deserialize<RpcResponse>(WriteResponseWithNearMissFields());

        Assert.Equal(79, response.MessageId);
        Assert.True(response.IsSuccess);
        Assert.Null(response.ErrorMessage);
        Assert.Null(response.ErrorType);
        Assert.Null(response.Stream);
    }

    [Theory]
    [InlineData("MessageId")]
    [InlineData("ServiceName")]
    [InlineData("MethodName")]
    [InlineData("InstanceId")]
    [InlineData("Streams")]
    public void RpcRequest_ReadsFieldNameSplitAcrossSequenceSegments(string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var bytes = Serialize(
            serializer,
            new RpcRequest
            {
                MessageId = 83,
                ServiceName = "SegmentedService",
                MethodName = "SegmentedMethod",
                InstanceId = "segmented-instance",
                Streams = [new RpcStreamHandle(83, RpcStreamKind.Items)],
            });

        var request = MessagePackSerializer.Deserialize<RpcRequest>(
            SplitInside(bytes, fieldName),
            serializer.Options);

        Assert.Equal(83, request.MessageId);
        Assert.Equal("SegmentedService", request.ServiceName);
        Assert.Equal("SegmentedMethod", request.MethodName);
        Assert.Equal("segmented-instance", request.InstanceId);
        Assert.Equal(83, Assert.Single(request.Streams!).StreamId);
    }

    [Theory]
    [InlineData("MessageId")]
    [InlineData("IsSuccess")]
    [InlineData("ErrorMessage")]
    [InlineData("ErrorType")]
    [InlineData("Stream")]
    public void RpcResponse_ReadsFieldNameSplitAcrossSequenceSegments(string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var responseWithField = fieldName[0] == 'E'
            ? new RpcResponse
            {
                MessageId = 89,
                IsSuccess = false,
                ErrorMessage = "segmented error",
                ErrorType = "SegmentedError",
            }
            : new RpcResponse
            {
                MessageId = 89,
                IsSuccess = true,
                Stream = new RpcStreamHandle(89, RpcStreamKind.Binary),
            };
        var bytes = Serialize(serializer, responseWithField);

        var response = MessagePackSerializer.Deserialize<RpcResponse>(
            SplitInside(bytes, fieldName),
            serializer.Options);

        Assert.Equal(responseWithField, response);
    }

    [Fact]
    public void RpcRequest_DuplicateFieldStillFails()
    {
        var serializer = new MessagePackRpcSerializer();

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(WriteRequestWithDuplicateMessageId()));

        Assert.Contains("duplicate MessageId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RpcResponse_DuplicateFieldStillFails()
    {
        var serializer = new MessagePackRpcSerializer();

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcResponse>(WriteResponseWithDuplicateSuccess()));

        Assert.Contains("duplicate IsSuccess", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] WriteRequestWithNearMissFields()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(8);
        WriteField(ref writer, "Streamx", 1);
        WriteField(ref writer, "MessageIx", 2);
        WriteField(ref writer, "InstanceIx", 3);
        WriteField(ref writer, "MethodNamo", 4);
        WriteField(ref writer, "ServiceNamo", 5);
        WriteField(ref writer, "MessageId", 73);
        WriteField(ref writer, "ServiceName", "ExactService");
        WriteField(ref writer, "MethodName", "ExactMethod");
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteResponseWithNearMissFields()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(7);
        WriteField(ref writer, "Streax", 1);
        WriteField(ref writer, "ErrorTypo", 2);
        WriteField(ref writer, "IsSuccesx", false);
        WriteField(ref writer, "MessageIx", 4);
        WriteField(ref writer, "ErrorMessago", 5);
        WriteField(ref writer, "MessageId", 79);
        WriteField(ref writer, "IsSuccess", true);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteRequestWithDuplicateMessageId()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(4);
        WriteField(ref writer, "MessageId", 1);
        WriteField(ref writer, "ServiceName", "Service");
        WriteField(ref writer, "MethodName", "Method");
        WriteField(ref writer, "MessageId", 2);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteResponseWithDuplicateSuccess()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(3);
        WriteField(ref writer, "MessageId", 1);
        WriteField(ref writer, "IsSuccess", true);
        WriteField(ref writer, "IsSuccess", false);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Serialize<T>(MessagePackRpcSerializer serializer, T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, value);
        return buffer.WrittenSpan.ToArray();
    }

    private static ReadOnlySequence<byte> SplitInside(byte[] bytes, string fieldName)
    {
        var fieldNameBytes = Encoding.UTF8.GetBytes(fieldName);
        var fieldStart = bytes.AsSpan().IndexOf(fieldNameBytes);
        Assert.True(fieldStart >= 0);
        var splitAt = fieldStart + fieldNameBytes.Length / 2;
        var first = new ByteSequenceSegment(bytes.AsMemory(0, splitAt));
        var last = first.Append(bytes.AsMemory(splitAt));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private static void WriteField(ref MessagePackWriter writer, string name, int value)
    {
        writer.Write(name);
        writer.Write(value);
    }

    private static void WriteField(ref MessagePackWriter writer, string name, bool value)
    {
        writer.Write(name);
        writer.Write(value);
    }

    private static void WriteField(ref MessagePackWriter writer, string name, string value)
    {
        writer.Write(name);
        writer.Write(value);
    }

    private sealed class ByteSequenceSegment : ReadOnlySequenceSegment<byte>
    {
        public ByteSequenceSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public ByteSequenceSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new ByteSequenceSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length,
            };
            Next = segment;
            return segment;
        }
    }
}
