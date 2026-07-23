namespace DotBoxD.Codecs.MessagePack;

internal static class RpcRequestNameValidation
{
    public static byte[]? ValidateRequestName(
        RpcRequestNameCache? requestNames,
        string? value,
        RpcRequestNameKind kind,
        string fieldName)
    {
        value = RequireValue(value, fieldName);
        if (requestNames is not null &&
            requestNames.TryGetRegistered(value, kind, out var registeredUtf8))
        {
            return registeredUtf8;
        }

        RpcEnvelopeStringValidation.ThrowIfMalformedUtf16(value, "request", fieldName);
        return null;
    }

    public static void ThrowIfMissingRequiredName(string? value, string fieldName)
    {
        value = RequireValue(value, fieldName);
        RpcEnvelopeStringValidation.ThrowIfMalformedUtf16(value, "request", fieldName);
    }

    private static string RequireValue(string? value, string fieldName)
    {
        if (value is null)
        {
            throw new RpcEnvelopeValidationException(
                $"RPC request is missing required {fieldName}.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RpcEnvelopeValidationException(
                $"RPC request contains empty or whitespace required {fieldName}.");
        }

        return value;
    }
}
