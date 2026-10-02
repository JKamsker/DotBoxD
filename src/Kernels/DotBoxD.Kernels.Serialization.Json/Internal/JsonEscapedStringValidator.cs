using System.Buffers;
using System.Text.Json;
using static DotBoxD.Kernels.Serialization.Json.JsonImport;

namespace DotBoxD.Kernels.Serialization.Json.Internal;

internal static class JsonEscapedStringValidator
{
    public static void Validate(ref Utf8JsonReader reader)
    {
        if (!reader.ValueIsEscaped)
        {
            return;
        }

        // Escaped surrogate sequences are accepted by Read(), but string materialization
        // rejects unpaired surrogates. Validate before the source map accesses those strings.
        byte[]? rented = null;
        Span<byte> buffer = reader.ValueSpan.Length <= 256
            ? stackalloc byte[256]
            : rented = ArrayPool<byte>.Shared.Rent(reader.ValueSpan.Length);
        try
        {
            reader.CopyString(buffer);
        }
        catch (InvalidOperationException)
        {
            throw Error("E-JSON-INVALID", "JSON IR contains an invalid escaped Unicode string");
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
