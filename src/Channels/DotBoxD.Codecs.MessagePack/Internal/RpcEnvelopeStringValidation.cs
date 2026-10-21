using MessagePack;

namespace DotBoxD.Codecs.MessagePack;

internal static class RpcEnvelopeStringValidation
{
    public static void ThrowIfMalformedUtf16(string? value, string envelopeName, string fieldName)
    {
        if (value is null)
        {
            return;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsHighSurrogate(current))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                throw MalformedUtf16(envelopeName, fieldName);
            }

            if (char.IsLowSurrogate(current))
            {
                throw MalformedUtf16(envelopeName, fieldName);
            }
        }
    }

    private static RpcEnvelopeValidationException MalformedUtf16(string envelopeName, string fieldName)
        => new(
            $"RPC {envelopeName} {fieldName} contains malformed UTF-16 text with an unpaired surrogate.");
}

internal sealed class RpcEnvelopeValidationException(string message) : MessagePackSerializationException(message)
{
}
