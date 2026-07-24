using System.Text;
using DotBoxD.Services.Protocol;
using MessagePack;
using MessagePack.Formatters;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class RpcResponseFormatter : IMessagePackFormatter<RpcResponse>
{
    public static readonly RpcResponseFormatter Instance = new();

    private static readonly byte[] MessageIdKey = Encoding.UTF8.GetBytes("MessageId");
    private static readonly byte[] IsSuccessKey = Encoding.UTF8.GetBytes("IsSuccess");
    private static readonly byte[] ErrorMessageKey = Encoding.UTF8.GetBytes("ErrorMessage");
    private static readonly byte[] ErrorTypeKey = Encoding.UTF8.GetBytes("ErrorType");
    private static readonly byte[] StreamKey = Encoding.UTF8.GetBytes("Stream");
    private RpcResponseFormatter()
    {
    }

    public void Serialize(
        ref MessagePackWriter writer,
        RpcResponse value,
        MessagePackSerializerOptions options)
    {
        ValidateEnvelope(value);
        writer.WriteMapHeader(5);
        writer.WriteString(MessageIdKey);
        writer.Write(value.MessageId);
        writer.WriteString(IsSuccessKey);
        writer.Write(value.IsSuccess);
        writer.WriteString(ErrorMessageKey);
        WriteNullableString(ref writer, value.ErrorMessage);
        writer.WriteString(ErrorTypeKey);
        WriteNullableString(ref writer, value.ErrorType);
        writer.WriteString(StreamKey);
        WriteNullableStream(ref writer, value.Stream, options);
    }

    public RpcResponse Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadMapHeader();
        var state = new RpcResponseReadState(options);

        for (var i = 0; i < count; i++)
        {
            state.ReadField(ReadField(ref reader), ref reader);
        }

        state.ValidateRequiredFields();
        ValidateEnvelope(state.Response);
        return state.Response;
    }

    // This state is local to synchronous deserialization, so keeping it as a mutable value type
    // avoids a fixed heap allocation without introducing shared state or lifetime concerns.
    private struct RpcResponseReadState(MessagePackSerializerOptions options)
    {
        private bool _seenMessageId;
        private bool _seenIsSuccess;
        private bool _seenErrorMessage;
        private bool _seenErrorType;
        private bool _seenStream;
        private RpcResponse _response;

        public RpcResponse Response => _response;

        public void ReadField(RpcResponseField field, ref MessagePackReader reader)
        {
            switch (field)
            {
                case RpcResponseField.MessageId:
                    ThrowIfDuplicate(_seenMessageId, nameof(RpcResponse.MessageId));
                    _seenMessageId = true;
                    _response.MessageId = reader.ReadInt32();
                    break;
                case RpcResponseField.IsSuccess:
                    ThrowIfDuplicate(_seenIsSuccess, nameof(RpcResponse.IsSuccess));
                    _seenIsSuccess = true;
                    _response.IsSuccess = reader.ReadBoolean();
                    break;
                case RpcResponseField.ErrorMessage:
                    ThrowIfDuplicate(_seenErrorMessage, nameof(RpcResponse.ErrorMessage));
                    _seenErrorMessage = true;
                    _response.ErrorMessage = reader.ReadString();
                    break;
                case RpcResponseField.ErrorType:
                    ThrowIfDuplicate(_seenErrorType, nameof(RpcResponse.ErrorType));
                    _seenErrorType = true;
                    _response.ErrorType = reader.ReadString();
                    break;
                case RpcResponseField.Stream:
                    ThrowIfDuplicate(_seenStream, nameof(RpcResponse.Stream));
                    _seenStream = true;
                    _response.Stream = ReadNullableStream(ref reader, options);
                    break;
                default:
                    MessagePackEnvelopeSkipper.SkipUnknownField(ref reader, "RPC response");
                    break;
            }
        }

        public void ValidateRequiredFields()
        {
            if (!_seenMessageId)
            {
                throw new RpcEnvelopeValidationException(
                    "RPC response is missing required MessageId.");
            }

            if (!_seenIsSuccess)
            {
                throw new RpcEnvelopeValidationException(
                    "RPC response is missing required IsSuccess.");
            }
        }
    }

    private static void ValidateEnvelope(RpcResponse response)
    {
        RpcEnvelopeStringValidation.ThrowIfMalformedUtf16(
            response.ErrorMessage,
            "response",
            nameof(RpcResponse.ErrorMessage));
        RpcEnvelopeStringValidation.ThrowIfMalformedUtf16(
            response.ErrorType,
            "response",
            nameof(RpcResponse.ErrorType));

        if (response.IsSuccess &&
            (response.ErrorMessage is not null || response.ErrorType is not null))
        {
            throw new RpcEnvelopeValidationException(
                "Successful RPC response must not contain error fields.");
        }

        if (!response.IsSuccess)
        {
            ThrowIfMissingOrBlankErrorDetail(
                response.ErrorMessage,
                nameof(RpcResponse.ErrorMessage));
            ThrowIfMissingOrBlankErrorDetail(
                response.ErrorType,
                nameof(RpcResponse.ErrorType));
        }

        if (!response.IsSuccess && response.Stream is not null)
        {
            throw new RpcEnvelopeValidationException(
                "Error RPC response must not contain a stream handle.");
        }
    }

    private static void ThrowIfMissingOrBlankErrorDetail(string? value, string fieldName)
    {
        if (value is null)
        {
            throw new RpcEnvelopeValidationException(
                $"Error RPC response is missing required {fieldName}.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RpcEnvelopeValidationException(
                $"Error RPC response contains blank {fieldName}.");
        }
    }

    private static void ThrowIfDuplicate(bool alreadySeen, string fieldName)
    {
        if (alreadySeen)
        {
            throw new RpcEnvelopeValidationException(
                $"RPC response contains duplicate {fieldName}.");
        }
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

    private static void WriteNullableStream(
        ref MessagePackWriter writer,
        RpcStreamHandle? value,
        MessagePackSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNil();
            return;
        }

        RpcStreamHandleFormatter.Instance.Serialize(
            ref writer,
            value.GetValueOrDefault(),
            options);
    }

    private static RpcStreamHandle? ReadNullableStream(
        ref MessagePackReader reader,
        MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        return RpcStreamHandleFormatter.Instance.Deserialize(ref reader, options);
    }

    private static RpcResponseField ReadField(ref MessagePackReader reader)
    {
        if (reader.TryReadStringSpan(out var utf8))
        {
            return ReadField(utf8);
        }

        return ReadField(reader.ReadString());
    }

    private static RpcResponseField ReadField(ReadOnlySpan<byte> utf8)
    {
        // Shape only selects a candidate; the full comparison remains the acceptance boundary.
        return utf8.Length switch
        {
            6 when utf8[0] == (byte)'S' => MatchField(utf8, StreamKey, RpcResponseField.Stream),
            9 when utf8[0] == (byte)'E' => MatchField(utf8, ErrorTypeKey, RpcResponseField.ErrorType),
            9 when utf8[0] == (byte)'I' => MatchField(utf8, IsSuccessKey, RpcResponseField.IsSuccess),
            9 when utf8[0] == (byte)'M' => MatchField(utf8, MessageIdKey, RpcResponseField.MessageId),
            12 when utf8[0] == (byte)'E' => MatchField(utf8, ErrorMessageKey, RpcResponseField.ErrorMessage),
            _ => RpcResponseField.Unknown,
        };
    }

    private static RpcResponseField ReadField(string? name)
    {
        if (name is null)
        {
            return RpcResponseField.Unknown;
        }

        return name.Length switch
        {
            6 when name[0] == 'S' => MatchField(name, "Stream", RpcResponseField.Stream),
            9 when name[0] == 'E' => MatchField(name, "ErrorType", RpcResponseField.ErrorType),
            9 when name[0] == 'I' => MatchField(name, "IsSuccess", RpcResponseField.IsSuccess),
            9 when name[0] == 'M' => MatchField(name, "MessageId", RpcResponseField.MessageId),
            12 when name[0] == 'E' => MatchField(name, "ErrorMessage", RpcResponseField.ErrorMessage),
            _ => RpcResponseField.Unknown,
        };
    }

    private static RpcResponseField MatchField(
        ReadOnlySpan<byte> candidate,
        ReadOnlySpan<byte> expected,
        RpcResponseField field) =>
        candidate.SequenceEqual(expected) ? field : RpcResponseField.Unknown;

    private static RpcResponseField MatchField(
        string candidate,
        string expected,
        RpcResponseField field) =>
        string.Equals(candidate, expected, StringComparison.Ordinal) ? field : RpcResponseField.Unknown;

    private enum RpcResponseField
    {
        Unknown,
        MessageId,
        IsSuccess,
        ErrorMessage,
        ErrorType,
        Stream,
    }
}
