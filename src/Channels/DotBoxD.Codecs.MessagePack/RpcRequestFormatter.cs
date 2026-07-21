using System.Text;
using DotBoxD.Services.Protocol;
using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class RpcRequestFormatter : IMessagePackFormatter<RpcRequest>
{
    private static readonly byte[] MessageIdKey = Encoding.UTF8.GetBytes("MessageId");
    private static readonly byte[] ServiceNameKey = Encoding.UTF8.GetBytes("ServiceName");
    private static readonly byte[] MethodNameKey = Encoding.UTF8.GetBytes("MethodName");
    private static readonly byte[] InstanceIdKey = Encoding.UTF8.GetBytes("InstanceId");
    private static readonly byte[] StreamsKey = Encoding.UTF8.GetBytes("Streams");
    private static readonly RpcRequestFieldName[] FieldNames =
    [
        new("MessageId", MessageIdKey, RpcRequestField.MessageId),
        new("ServiceName", ServiceNameKey, RpcRequestField.ServiceName),
        new("MethodName", MethodNameKey, RpcRequestField.MethodName),
        new("InstanceId", InstanceIdKey, RpcRequestField.InstanceId),
        new("Streams", StreamsKey, RpcRequestField.Streams)
    ];
    private RpcRequestNameCache? _requestNames;

    private RpcRequestNameCache RequestNames => LazyInitializer.EnsureInitialized(ref _requestNames)!;

    public void Serialize(
        ref MessagePackWriter writer,
        RpcRequest value,
        MessagePackSerializerOptions options)
    {
        ThrowIfMissingRequiredName(value.ServiceName, nameof(RpcRequest.ServiceName));
        ThrowIfMissingRequiredName(value.MethodName, nameof(RpcRequest.MethodName));
        RpcEnvelopeStringValidation.ThrowIfMalformedUtf16(
            value.InstanceId,
            "request",
            nameof(RpcRequest.InstanceId));
        var requestNames = RequestNames;
        requestNames.Register(value.ServiceName, RpcRequestNameKind.Service);
        requestNames.Register(value.MethodName, RpcRequestNameKind.Method);

        writer.WriteMapHeader(5);
        writer.WriteString(MessageIdKey);
        writer.Write(value.MessageId);
        writer.WriteString(ServiceNameKey);
        WriteNullableString(ref writer, value.ServiceName);
        writer.WriteString(MethodNameKey);
        WriteNullableString(ref writer, value.MethodName);
        writer.WriteString(InstanceIdKey);
        WriteNullableString(ref writer, value.InstanceId);
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

    private static void ThrowIfEmptyOrWhitespaceRequiredName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RpcEnvelopeValidationException(
                $"RPC request contains empty or whitespace required {fieldName}.");
        }
    }

    private static void ThrowIfMissingRequiredName(string? value, string fieldName)
    {
        if (value is null)
        {
            throw new RpcEnvelopeValidationException(
                $"RPC request is missing required {fieldName}.");
        }

        ThrowIfEmptyOrWhitespaceRequiredName(value, fieldName);
        RpcEnvelopeStringValidation.ThrowIfMalformedUtf16(value, "request", fieldName);
    }

    private static void WriteNullableString(ref MessagePackWriter writer, string? value)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        writer.Write(value);
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
        foreach (var field in FieldNames)
        {
            if (utf8.SequenceEqual(field.Utf8Name))
            {
                return field.Field;
            }
        }

        return RpcRequestField.Unknown;
    }

    private static RpcRequestField ReadField(string? name)
    {
        foreach (var field in FieldNames)
        {
            if (string.Equals(name, field.Name, StringComparison.Ordinal))
            {
                return field.Field;
            }
        }

        return RpcRequestField.Unknown;
    }

    private enum RpcRequestField
    {
        Unknown,
        MessageId,
        ServiceName,
        MethodName,
        InstanceId,
        Streams,
    }

    private readonly struct RpcRequestFieldName
    {
        public RpcRequestFieldName(string name, byte[] utf8Name, RpcRequestField field)
        {
            Name = name;
            Utf8Name = utf8Name;
            Field = field;
        }

        public string Name { get; }

        public byte[] Utf8Name { get; }

        public RpcRequestField Field { get; }
    }
}
