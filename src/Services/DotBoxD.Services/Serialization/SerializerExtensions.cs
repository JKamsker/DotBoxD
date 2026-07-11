using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Serialization;

/// <summary>
/// Helpers for serializing directly into pooled buffers.
/// </summary>
public static class SerializerExtensions
{
    /// <summary>
    /// Serializes <paramref name="value"/> into a freshly rented <see cref="Payload"/>. The caller
    /// owns the returned payload and must dispose it.
    /// </summary>
    public static Payload SerializeToPayload<T>(this ISerializer serializer, T value)
    {
        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        using var writer = PooledBufferWriter.Rent();
        serializer.Serialize(writer, value);
        return writer.DetachPayload();
    }
}
