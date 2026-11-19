using System.Text.Json;
using static DotBoxD.Kernels.Serialization.Json.JsonImport;

namespace DotBoxD.Kernels.Serialization.Json.Internal;

internal static class JsonEscapedStringValidator
{
    /// <summary>
    /// Validates the current token's escape sequences. Safe to call for every token:
    /// only string and property-name tokens can be escaped.
    /// </summary>
    public static void Validate(ref Utf8JsonReader reader)
    {
        if (!reader.ValueIsEscaped)
        {
            return;
        }

        // Escaped surrogate sequences are accepted by Read(), but string materialization
        // rejects unpaired surrogates. Validate before the source map accesses those strings.
        try
        {
            _ = reader.GetString();
        }
        catch (InvalidOperationException)
        {
            throw Error("E-JSON-INVALID", "JSON IR contains an invalid escaped Unicode string");
        }
    }
}
