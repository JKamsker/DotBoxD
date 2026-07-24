using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.RequestNames;

public sealed class RpcRequestNameSerializationTests
{
    [Theory]
    [InlineData(false, 31)]
    [InlineData(false, 32)]
    [InlineData(false, 255)]
    [InlineData(false, 256)]
    [InlineData(false, 257)]
    [InlineData(true, 31)]
    [InlineData(true, 32)]
    [InlineData(true, 255)]
    [InlineData(true, 256)]
    [InlineData(true, 257)]
    public void Ascii_names_match_canonical_wire_bytes(bool oldSpec, int length)
    {
        var request = Request(new string('S', length), new string('M', length));

        Assert.Equal(SerializeCanonical(request, oldSpec), Serialize(request, oldSpec));
    }

    [Theory]
    [InlineData(false, "\u00e9", 128)]
    [InlineData(false, "\u00e9", 129)]
    [InlineData(false, "\U0001f600", 64)]
    [InlineData(false, "\U0001f600", 65)]
    [InlineData(true, "\u00e9", 128)]
    [InlineData(true, "\u00e9", 129)]
    [InlineData(true, "\U0001f600", 64)]
    [InlineData(true, "\U0001f600", 65)]
    public void Multibyte_names_match_canonical_wire_bytes(
        bool oldSpec,
        string character,
        int characterCount)
    {
        var serviceName = Repeat(character, characterCount);
        var methodName = Repeat(character == "\u00e9" ? "\u00f8" : "\U0001f680", characterCount);
        var request = Request(serviceName, methodName);

        Assert.Equal(SerializeCanonical(request, oldSpec), Serialize(request, oldSpec));
    }

    [Fact]
    public void Registration_capacity_fallback_matches_canonical_wire_bytes()
    {
        var serializer = new MessagePackRpcSerializer();
        for (var i = 0; i < 64; i++)
        {
            _ = Serialize(serializer, Request($"Service{i:D3}", $"Method{i:D3}"));
        }

        var request = Request(new string('\u00e9', 64), new string('\u00f8', 64));

        Assert.Equal(SerializeCanonical(request, oldSpec: false), Serialize(serializer, request));
    }

    [Fact]
    public void Concurrent_first_registration_publishes_canonical_bytes()
    {
        var serializer = new MessagePackRpcSerializer();
        var request = Request(new string('\u00e9', 128), Repeat("\U0001f680", 64));
        var expected = SerializeCanonical(request, oldSpec: false);
        var actual = new byte[64][];

        Parallel.For(0, actual.Length, index => actual[index] = Serialize(serializer, request));

        Assert.All(actual, bytes => Assert.Equal(expected, bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_value_distinct_registered_names_match_canonical_wire_bytes(bool oldSpec)
    {
        var serializer = new MessagePackRpcSerializer(
            MessagePackRpcSerializer.CreateOptions().WithOldSpec(oldSpec));
        var registered = Request(new string('S', 128), new string('M', 128));
        var distinct = Request(
            new string(registered.ServiceName.ToCharArray()),
            new string(registered.MethodName.ToCharArray()));
        var expected = SerializeCanonical(registered, oldSpec);

        Assert.Equal(expected, Serialize(serializer, registered));
        Assert.NotSame(registered.ServiceName, distinct.ServiceName);
        Assert.NotSame(registered.MethodName, distinct.MethodName);
        Assert.Equal(expected, Serialize(serializer, distinct));
    }

    [Theory]
    [MemberData(nameof(InvalidWarmCacheRequiredNames))]
    public void Registered_names_do_not_bypass_required_name_validation(
        string fieldName,
        string invalidValueKind)
    {
        var serializer = new MessagePackRpcSerializer();
        var valid = Request(
            new string("RegisteredService".ToCharArray()),
            new string("RegisteredMethod".ToCharArray()));
        var expected = SerializeCanonical(valid, oldSpec: false);
        Assert.Equal(expected, Serialize(serializer, valid));
        Assert.Equal(expected, Serialize(serializer, valid));
        var invalidValue = InvalidRequiredName(invalidValueKind);

        var invalid = Request(
            fieldName == nameof(RpcRequest.ServiceName) ? invalidValue! : valid.ServiceName,
            fieldName == nameof(RpcRequest.MethodName) ? invalidValue! : valid.MethodName);
        var writer = new ArrayBufferWriter<byte>();

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, invalid));

        Assert.Contains(fieldName, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, writer.WrittenCount);
        Assert.Equal(expected, Serialize(serializer, valid));
    }

    [Fact]
    public void Malformed_first_registration_does_not_poison_later_valid_names()
    {
        var serializer = new MessagePackRpcSerializer();
        var malformed = Request("Service" + new string('\uD800', 1), "Method");

        Assert.Throws<MessagePackSerializationException>(() => Serialize(serializer, malformed));

        var valid = Request("Service", "Method");
        var expected = SerializeCanonical(valid, oldSpec: false);
        Assert.Equal(expected, Serialize(serializer, valid));
        Assert.Equal(expected, Serialize(serializer, valid));
    }

    [Theory]
    [InlineData(nameof(RpcRequest.MethodName))]
    [InlineData(nameof(RpcRequest.InstanceId))]
    public void Cached_names_do_not_bypass_later_field_validation(string malformedField)
    {
        var serializer = new MessagePackRpcSerializer();
        var valid = Request("Service", "Method");
        valid.InstanceId = "Instance";
        var expected = SerializeCanonical(valid, oldSpec: false);
        Assert.Equal(expected, Serialize(serializer, valid));

        var malformedText = "invalid" + new string('\uD800', 1);
        var malformed = Request(
            valid.ServiceName,
            malformedField == nameof(RpcRequest.MethodName) ? malformedText : valid.MethodName);
        malformed.InstanceId = malformedField == nameof(RpcRequest.InstanceId)
            ? malformedText
            : valid.InstanceId;
        var writer = new ArrayBufferWriter<byte>();

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, malformed));

        Assert.Contains(malformedField, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, writer.WrittenCount);
        Assert.Equal(expected, Serialize(serializer, valid));
    }

    public static TheoryData<string, string> InvalidWarmCacheRequiredNames() =>
        new()
        {
            { nameof(RpcRequest.ServiceName), "null" },
            { nameof(RpcRequest.ServiceName), "empty" },
            { nameof(RpcRequest.ServiceName), "whitespace" },
            { nameof(RpcRequest.ServiceName), "malformed" },
            { nameof(RpcRequest.MethodName), "null" },
            { nameof(RpcRequest.MethodName), "empty" },
            { nameof(RpcRequest.MethodName), "whitespace" },
            { nameof(RpcRequest.MethodName), "malformed" },
        };

    private static string? InvalidRequiredName(string kind) =>
        kind switch
        {
            "null" => null,
            "empty" => string.Empty,
            "whitespace" => " \t\u2003",
            "malformed" => "invalid" + new string('\uD800', 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown invalid-name case."),
        };

    private static RpcRequest Request(string serviceName, string methodName) =>
        new()
        {
            MessageId = 42,
            ServiceName = serviceName,
            MethodName = methodName,
        };

    private static byte[] Serialize(RpcRequest request, bool oldSpec)
    {
        var options = MessagePackRpcSerializer.CreateOptions().WithOldSpec(oldSpec);
        return Serialize(new MessagePackRpcSerializer(options), request);
    }

    private static byte[] Serialize(MessagePackRpcSerializer serializer, RpcRequest request)
    {
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(buffer, request);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] SerializeCanonical(RpcRequest request, bool oldSpec)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer) { OldSpec = oldSpec };
        writer.WriteMapHeader(5);
        writer.Write("MessageId");
        writer.Write(request.MessageId);
        writer.Write("ServiceName");
        writer.Write(request.ServiceName);
        writer.Write("MethodName");
        writer.Write(request.MethodName);
        writer.Write("InstanceId");
        writer.Write(request.InstanceId);
        writer.Write("Streams");
        writer.WriteNil();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static string Repeat(string value, int count) => string.Concat(Enumerable.Repeat(value, count));
}
