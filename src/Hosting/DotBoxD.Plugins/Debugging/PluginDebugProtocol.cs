using System.Text.Json;

namespace DotBoxD.Plugins.Debugging;

/// <summary>Bounded UTF-8 JSON codec for the frozen version-one debug endpoint.</summary>
public static class PluginDebugProtocol
{
    private const int MaxEnvelopeTextLength = 128;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 32,
        PropertyNameCaseInsensitive = false
    };

    public const int Version = 1;

    public static byte[] Encode(PluginDebugEnvelope envelope, int maxMessageBytes)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        EnsureLimit(maxMessageBytes);
        ValidateEnvelope(envelope);
        var encoded = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        if (encoded.Length > maxMessageBytes)
        {
            throw Error("messageTooLarge", $"The debug message exceeds the {maxMessageBytes}-byte limit.");
        }

        return encoded;
    }

    public static PluginDebugEnvelope Decode(ReadOnlyMemory<byte> message, int maxMessageBytes)
    {
        EnsureLimit(maxMessageBytes);
        if (message.IsEmpty)
        {
            throw Error("invalidMessage", "The debug message is empty.");
        }

        if (message.Length > maxMessageBytes)
        {
            throw Error("messageTooLarge", $"The debug message exceeds the {maxMessageBytes}-byte limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                message,
                new JsonDocumentOptions { MaxDepth = SerializerOptions.MaxDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Error("invalidMessage", "The debug message must contain a JSON object.");
            }

            RejectDuplicateEnvelopeFields(document.RootElement);
            var envelope = new PluginDebugEnvelope(
                RequiredInt32(document.RootElement, "version"),
                RequiredString(document.RootElement, "kind"),
                RequiredString(document.RootElement, "id"),
                RequiredString(document.RootElement, "sessionToken"),
                RequiredProperty(document.RootElement, "payload"));
            ValidateEnvelope(envelope);
            return envelope;
        }
        catch (PluginDebugProtocolException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw Error("invalidMessage", "The debug message is not valid bounded UTF-8 JSON.");
        }
        catch (NotSupportedException)
        {
            throw Error("invalidMessage", "The debug message contains an unsupported JSON value.");
        }
        catch (InvalidOperationException)
        {
            throw Error("invalidMessage", "The debug message contains a field with an invalid JSON type.");
        }
        catch (FormatException)
        {
            throw Error("invalidMessage", "The debug message contains a field with an invalid value.");
        }
    }

    private static void ValidateEnvelope(PluginDebugEnvelope envelope)
    {
        if (envelope.Version <= 0)
        {
            throw Error("invalidVersion", "The debug protocol version must be positive.");
        }

        ValidateText(envelope.Kind, "kind");
        ValidateText(envelope.Id, "id");
        ValidateText(envelope.SessionToken, "sessionToken");
        if (envelope.Payload.ValueKind == JsonValueKind.Undefined)
        {
            throw Error("invalidMessage", "The debug message payload is required.");
        }
    }

    private static void RejectDuplicateEnvelopeFields(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw Error("invalidMessage", $"The debug message contains duplicate '{property.Name}' fields.");
            }
        }
    }

    private static int RequiredInt32(JsonElement root, string name)
    {
        var value = RequiredProperty(root, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw Error("invalidMessage", $"The debug message {name} must be a 32-bit integer.");
        }

        return result;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = RequiredProperty(root, name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Error("invalidMessage", $"The debug message {name} must be a string.");
        }

        return value.GetString()!;
    }

    private static JsonElement RequiredProperty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw Error("invalidMessage", $"The debug message {name} is required.");
        }

        return value;
    }

    private static void ValidateText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxEnvelopeTextLength)
        {
            throw Error(
                "invalidMessage",
                $"The debug message {name} must contain between 1 and {MaxEnvelopeTextLength} characters.");
        }
    }

    private static void EnsureLimit(int maxMessageBytes)
    {
        if (maxMessageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMessageBytes),
                maxMessageBytes,
                "The debug message limit must be positive.");
        }
    }

    private static PluginDebugProtocolException Error(string code, string message) => new(code, message);
}
