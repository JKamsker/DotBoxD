using System.Buffers.Binary;
using System.IO.Compression;

namespace DotBoxD.UI.Runtime;

internal static class UiPngEncoder
{
    public static byte[] Encode(UiImageResource image)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8;
        header[9] = 6; // Eight-bit RGBA, no interlacing.
        Chunk(output, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var stride = image.Width * 4;
            for (var row = 0; row < image.Height; row++)
            {
                zlib.WriteByte(0); // Unfiltered rows.
                zlib.Write(image.Rgba.AsSpan(row * stride, stride));
            }
        }
        Chunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> bytes)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length);
        output.Write(number);
        output.Write(type);
        output.Write(bytes);
        var crc = Update(uint.MaxValue, type);
        crc = Update(crc, bytes) ^ uint.MaxValue;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            { crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320); }
        }
        return crc;
    }
}
