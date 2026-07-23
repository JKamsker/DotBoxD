using MessagePack;

namespace DotBoxD.Codecs.MessagePack;

internal static class RpcRequestNameWriter
{
    public static void Write(ref MessagePackWriter writer, string? value, byte[]? utf8)
    {
        // The span overload emits Str8 for 32-255 bytes even in OldSpec mode, where
        // the canonical string overload uses Str16. Preserve that legacy wire format.
        if (utf8 is not null && !writer.OldSpec)
        {
            writer.WriteString(utf8);
            return;
        }

        writer.Write(value);
    }
}
