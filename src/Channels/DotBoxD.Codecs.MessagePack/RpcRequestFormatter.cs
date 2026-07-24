using System.Text;
using DotBoxD.Services.Protocol;
using MessagePack;
using MessagePack.Formatters;
using static DotBoxD.Codecs.MessagePack.RpcRequestNameValidation;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class RpcRequestFormatter : IMessagePackFormatter<RpcRequest>
{
    private static readonly byte[] MessageIdKey = Encoding.UTF8.GetBytes("MessageId");
    private static readonly byte[] ServiceNameKey = Encoding.UTF8.GetBytes("ServiceName");
    private static readonly byte[] MethodNameKey = Encoding.UTF8.GetBytes("MethodName");
    private static readonly byte[] InstanceIdKey = Encoding.UTF8.GetBytes("InstanceId");
    private static readonly byte[] StreamsKey = Encoding.UTF8.GetBytes("Streams");
    private RpcRequestNameCache? _requestNames;

    private RpcRequestNameCache RequestNames => LazyInitializer.EnsureInitialized(ref _requestNames)!;

    public void Serialize(
        ref MessagePackWriter writer,
        RpcRequest value,
        MessagePackSerializerOptions options)
    {
        var requestNames = Volatile.Read(ref _requestNames);
        var serviceNameUtf8 = ValidateRequestName(
            requestNames,
            value.ServiceName,
            RpcRequestNameKind.Service,
            nameof(RpcRequest.ServiceName));
        var methodNameUtf8 = ValidateRequestName(
            requestNames,
            value.MethodName,
            RpcRequestNameKind.Method,
            nameof(RpcRequest.MethodName));
        RpcEnvelopeStringValidation.ThrowIfMalformedUtf16(
            value.InstanceId,
            "request",
            nameof(RpcRequest.InstanceId));
        requestNames ??= RequestNames;
        serviceNameUtf8 ??= requestNames.Register(value.ServiceName, RpcRequestNameKind.Service);
        methodNameUtf8 ??= requestNames.Register(value.MethodName, RpcRequestNameKind.Method);

        writer.WriteMapHeader(5);
        writer.WriteString(MessageIdKey);
        writer.Write(value.MessageId);
        writer.WriteString(ServiceNameKey);
        RpcRequestNameWriter.Write(ref writer, value.ServiceName, serviceNameUtf8);
        writer.WriteString(MethodNameKey);
        RpcRequestNameWriter.Write(ref writer, value.MethodName, methodNameUtf8);
        writer.WriteString(InstanceIdKey);
        writer.Write(value.InstanceId);
        writer.WriteString(StreamsKey);
        WriteStreams(ref writer, value.Streams, options);
    }

    public RpcRequest Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadMapHeader();
        var state = new RpcRequestReadState(options, RequestNames);

        for (var i = 0; i < count; i++)
        {
            state.ReadField(ReadField(ref reader), ref reader);
        }

        state.ValidateRequiredFields();
        return state.Request;
    }

    // This state is local to synchronous deserialization, so keeping it as a mutable value type
    // avoids a fixed heap allocation without introducing shared state or lifetime concerns.
    private struct RpcRequestReadState(
        MessagePackSerializerOptions options,
        RpcRequestNameCache requestNames)
    {
        private bool _seenMessageId;
        private bool _seenServiceName;
        private bool _seenMethodName;
        private bool _seenInstanceId;
        private bool _seenStreams;
        private RpcRequest _request = new();

        public RpcRequest Request => _request;

        public void ReadField(RpcRequestField field, ref MessagePackReader reader)
        {
            switch (field)
            {
                case RpcRequestField.MessageId:
                    ThrowIfDuplicate(_seenMessageId, nameof(RpcRequest.MessageId));
                    _seenMessageId = true;
                    _request.MessageId = reader.ReadInt32();
                    break;
                case RpcRequestField.ServiceName:
                    ThrowIfDuplicate(_seenServiceName, nameof(RpcRequest.ServiceName));
                    _seenServiceName = true;
                    _request.ServiceName = ReadCachedName(ref reader, requestNames, RpcRequestNameKind.Service)!;
                    break;
                case RpcRequestField.MethodName:
                    ThrowIfDuplicate(_seenMethodName, nameof(RpcRequest.MethodName));
                    _seenMethodName = true;
                    _request.MethodName = ReadCachedName(ref reader, requestNames, RpcRequestNameKind.Method)!;
                    break;
                case RpcRequestField.InstanceId:
                    ThrowIfDuplicate(_seenInstanceId, nameof(RpcRequest.InstanceId));
                    _seenInstanceId = true;
                    _request.InstanceId = reader.ReadString();
                    break;
                case RpcRequestField.Streams:
                    ThrowIfDuplicate(_seenStreams, nameof(RpcRequest.Streams));
                    _seenStreams = true;
                    _request.Streams = ReadStreams(ref reader, options);
                    break;
                default:
                    MessagePackEnvelopeSkipper.SkipUnknownField(ref reader, "RPC request");
                    break;
            }
        }

        public void ValidateRequiredFields()
        {
            if (!_seenMessageId)
            {
                throw new RpcEnvelopeValidationException(
                    "RPC request is missing required MessageId.");
            }

            ThrowIfMissingRequiredName(_seenServiceName ? _request.ServiceName : null, nameof(RpcRequest.ServiceName));
            ThrowIfMissingRequiredName(_seenMethodName ? _request.MethodName : null, nameof(RpcRequest.MethodName));
            RpcEnvelopeStringValidation.ThrowIfMalformedUtf16(
                _request.InstanceId,
                "request",
                nameof(RpcRequest.InstanceId));
        }
    }

    private static void WriteStreams(
        ref MessagePackWriter writer,
        RpcStreamHandle[]? streams,
        MessagePackSerializerOptions options)
    {
        if (streams is null)
        {
            writer.WriteNil();
            return;
        }

        writer.WriteArrayHeader(streams.Length);
        foreach (var stream in streams)
        {
            RpcStreamHandleFormatter.Instance.Serialize(ref writer, stream, options);
        }
    }

    private static RpcStreamHandle[]? ReadStreams(
        ref MessagePackReader reader,
        MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        options.Security.DepthStep(ref reader);
        try
        {
            var count = reader.ReadArrayHeader();
            var streams = new RpcStreamHandle[count];
            for (var i = 0; i < count; i++)
            {
                streams[i] = RpcStreamHandleFormatter.Instance.Deserialize(ref reader, options);
            }

            return streams;
        }
        finally
        {
            reader.Depth--;
        }
    }

    private static void ThrowIfDuplicate(bool alreadySeen, string fieldName)
    {
        if (alreadySeen)
        {
            throw new RpcEnvelopeValidationException(
                $"RPC request contains duplicate {fieldName}.");
        }
    }

    private static string? ReadCachedName(
        ref MessagePackReader reader,
        RpcRequestNameCache requestNames,
        RpcRequestNameKind kind)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        return reader.TryReadStringSpan(out var utf8)
            ? requestNames.GetOrAdd(utf8, kind)
            : requestNames.GetOrAdd(reader.ReadString()!, kind);
    }

    private static RpcRequestField ReadField(ref MessagePackReader reader)
    {
        if (reader.TryReadStringSpan(out var utf8))
        {
            return ReadField(utf8);
        }

        return ReadField(reader.ReadString());
    }

    private static RpcRequestField ReadField(ReadOnlySpan<byte> utf8)
    {
        // Shape only selects a candidate; the full comparison remains the acceptance boundary.
        return utf8.Length switch
        {
            7 when utf8[0] == (byte)'S' => MatchField(utf8, StreamsKey, RpcRequestField.Streams),
            9 when utf8[0] == (byte)'M' => MatchField(utf8, MessageIdKey, RpcRequestField.MessageId),
            10 when utf8[0] == (byte)'I' => MatchField(utf8, InstanceIdKey, RpcRequestField.InstanceId),
            10 when utf8[0] == (byte)'M' => MatchField(utf8, MethodNameKey, RpcRequestField.MethodName),
            11 when utf8[0] == (byte)'S' => MatchField(utf8, ServiceNameKey, RpcRequestField.ServiceName),
            _ => RpcRequestField.Unknown,
        };
    }

    private static RpcRequestField ReadField(string? name)
    {
        if (name is null)
        {
            return RpcRequestField.Unknown;
        }

        return name.Length switch
        {
            7 when name[0] == 'S' => MatchField(name, "Streams", RpcRequestField.Streams),
            9 when name[0] == 'M' => MatchField(name, "MessageId", RpcRequestField.MessageId),
            10 when name[0] == 'I' => MatchField(name, "InstanceId", RpcRequestField.InstanceId),
            10 when name[0] == 'M' => MatchField(name, "MethodName", RpcRequestField.MethodName),
            11 when name[0] == 'S' => MatchField(name, "ServiceName", RpcRequestField.ServiceName),
            _ => RpcRequestField.Unknown,
        };
    }

    private static RpcRequestField MatchField(
        ReadOnlySpan<byte> candidate,
        ReadOnlySpan<byte> expected,
        RpcRequestField field) =>
        candidate.SequenceEqual(expected) ? field : RpcRequestField.Unknown;

    private static RpcRequestField MatchField(
        string candidate,
        string expected,
        RpcRequestField field) =>
        string.Equals(candidate, expected, StringComparison.Ordinal) ? field : RpcRequestField.Unknown;

    private enum RpcRequestField
    {
        Unknown,
        MessageId,
        ServiceName,
        MethodName,
        InstanceId,
        Streams,
    }
}
