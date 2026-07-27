using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using MessagePack;
using MessagePack.Formatters;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public class MessagePackRpcSerializerTests
{
    [Fact]
    public void ReadOnlyMemoryByteFields_RoundTripAsBinaryPayload()
    {
        var serializer = new MessagePackRpcSerializer();
        var dto = new BinaryDto { Data = new byte[] { 1, 2, 3, 4 } };

        using var payload = serializer.SerializeToPayload(dto);
        var roundTrip = serializer.Deserialize<BinaryDto>(payload.Memory);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, roundTrip.Data.ToArray());
    }

    [Fact]
    public void RepeatedShortPayloadStrings_AreNotGloballyCached()
    {
        var serializer = new MessagePackRpcSerializer();

        var first = RoundTrip(serializer, "player-1");
        var second = RoundTrip(serializer, "player-1");

        Assert.Equal("player-1", first);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void RpcRequestNames_RoundTripToCachedReference()
    {
        var serializer = new MessagePackRpcSerializer();

        var first = RoundTrip(serializer, CreateRequest(1, "GameService", "MovePlayerAsync"));
        var second = RoundTrip(serializer, CreateRequest(2, "GameService", "MovePlayerAsync"));

        Assert.Equal("GameService", first.ServiceName);
        Assert.Equal("MovePlayerAsync", first.MethodName);
        Assert.Same(first.ServiceName, second.ServiceName);
        Assert.Same(first.MethodName, second.MethodName);
    }

    [Fact]
    public void RecurringRemoteRequestNames_RecoverAfterBoundedCacheChurn()
    {
        var serializer = new MessagePackRpcSerializer();
        AddRemoteNameChurn(serializer, 512);
        var payload = SerializeRawRequest("RecoveredService", "RecoveredMethod");

        _ = serializer.Deserialize<RpcRequest>(payload);
        var first = serializer.Deserialize<RpcRequest>(payload);
        var second = serializer.Deserialize<RpcRequest>(payload);

        Assert.Same(first.ServiceName, second.ServiceName);
        Assert.Same(first.MethodName, second.MethodName);
    }

    [Fact]
    public void RegisteredRequestNames_SurviveRemoteCacheChurn()
    {
        var serializer = new MessagePackRpcSerializer();
        var serviceName = new string("ProtectedService".ToCharArray());
        var methodName = new string("ProtectedMethod".ToCharArray());
        var request = CreateRequest(1, serviceName, methodName);
        var registered = RoundTrip(serializer, request);
        AddRemoteNameChurn(serializer, 512);

        var decoded = serializer.Deserialize<RpcRequest>(
            SerializeRawRequest(serviceName, methodName));

        Assert.Same(request.ServiceName, registered.ServiceName);
        Assert.Same(request.MethodName, registered.MethodName);
        Assert.Same(request.ServiceName, decoded.ServiceName);
        Assert.Same(request.MethodName, decoded.MethodName);
    }

    [Fact]
    public void IndependentSerializers_DoNotShareRemoteNameCachePressure()
    {
        var noisySerializer = new MessagePackRpcSerializer();
        AddRemoteNameChurn(noisySerializer, 512);
        var isolatedSerializer = new MessagePackRpcSerializer();
        var payload = SerializeRawRequest("IsolatedService", "IsolatedMethod");

        var first = isolatedSerializer.Deserialize<RpcRequest>(payload);
        var second = isolatedSerializer.Deserialize<RpcRequest>(payload);

        Assert.Same(first.ServiceName, second.ServiceName);
        Assert.Same(first.MethodName, second.MethodName);
    }

    [Fact]
    public void ConcurrentRemoteRequestNameAdmission_ConvergesOnStableReferences()
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = SerializeRawRequest("ConcurrentService", "ConcurrentMethod");
        var requests = new RpcRequest[64];

        Parallel.For(
            0,
            requests.Length,
            index => requests[index] = serializer.Deserialize<RpcRequest>(payload));

        var first = requests[0];
        Assert.All(requests, request =>
        {
            Assert.Same(first.ServiceName, request.ServiceName);
            Assert.Same(first.MethodName, request.MethodName);
        });
    }

    [Fact]
    public void LongRpcRequestNames_AreNotCached()
    {
        var serializer = new MessagePackRpcSerializer();
        var serviceName = new string('S', 300);
        var methodName = new string('M', 300);

        var first = RoundTrip(serializer, CreateRequest(1, serviceName, methodName));
        var second = RoundTrip(serializer, CreateRequest(2, serviceName, methodName));

        Assert.Equal(serviceName, first.ServiceName);
        Assert.Equal(methodName, first.MethodName);
        Assert.NotSame(first.ServiceName, second.ServiceName);
        Assert.NotSame(first.MethodName, second.MethodName);
    }

    [Theory]
    [InlineData("a", 256, true)]
    [InlineData("a", 257, false)]
    [InlineData("é", 128, true)]
    [InlineData("é", 129, false)]
    public void RemoteRequestNames_RespectUtf8CacheBoundary(
        string character,
        int characterCount,
        bool expectedCached)
    {
        var serializer = new MessagePackRpcSerializer();
        var name = string.Concat(Enumerable.Repeat(character, characterCount));
        var payload = SerializeRawRequest(name, name);

        var first = serializer.Deserialize<RpcRequest>(payload);
        var second = serializer.Deserialize<RpcRequest>(payload);

        Assert.Equal(expectedCached, ReferenceEquals(first.ServiceName, second.ServiceName));
        Assert.Equal(expectedCached, ReferenceEquals(first.MethodName, second.MethodName));
    }

    [Fact]
    public void LongStrings_AreNotCached()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new string('x', 300);

        var first = RoundTrip(serializer, value);
        var second = RoundTrip(serializer, value);

        Assert.Equal(value, first);
        Assert.Equal(value, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void CustomStringFormatter_TakesPrecedenceOverDefaultResolvers()
    {
        var resolver = new CustomStringResolver();
        var serializer = MessagePackRpcSerializer.CreateWithResolver(resolver);

        var result = RoundTrip(serializer, "player-1");

        Assert.Equal("custom:player-1", result);
        Assert.Equal(1, resolver.Formatter.DeserializeCalls);
    }

    private static T RoundTrip<T>(MessagePackRpcSerializer serializer, T value)
    {
        using var payload = serializer.SerializeToPayload(value);
        return serializer.Deserialize<T>(payload.Memory);
    }

    private static RpcRequest CreateRequest(int messageId, string serviceName, string methodName) =>
        new()
        {
            MessageId = messageId,
            ServiceName = new string(serviceName.ToCharArray()),
            MethodName = new string(methodName.ToCharArray()),
        };

    private static void AddRemoteNameChurn(MessagePackRpcSerializer serializer, int requestCount)
    {
        for (var i = 0; i < requestCount; i++)
        {
            _ = serializer.Deserialize<RpcRequest>(
                SerializeRawRequest($"ChurnService{i:D4}", $"ChurnMethod{i:D4}"));
        }
    }

    private static ReadOnlyMemory<byte> SerializeRawRequest(string serviceName, string methodName)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(3);
        writer.Write("MessageId");
        writer.Write(42);
        writer.Write("ServiceName");
        writer.Write(serviceName);
        writer.Write("MethodName");
        writer.Write(methodName);
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }

    public sealed class BinaryDto
    {
        public ReadOnlyMemory<byte> Data { get; set; }
    }

    private sealed class CustomStringResolver : IFormatterResolver
    {
        public CustomStringFormatter Formatter { get; } = new();

        public IMessagePackFormatter<T>? GetFormatter<T>() =>
            typeof(T) == typeof(string)
                ? (IMessagePackFormatter<T>)(object)Formatter
                : null;
    }

    internal sealed class CustomStringFormatter : IMessagePackFormatter<string?>
    {
        public int DeserializeCalls { get; private set; }

        public void Serialize(
            ref MessagePackWriter writer,
            string? value,
            MessagePackSerializerOptions options) =>
            writer.Write(value);

        public string? Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            DeserializeCalls++;
            return "custom:" + reader.ReadString();
        }
    }
}
