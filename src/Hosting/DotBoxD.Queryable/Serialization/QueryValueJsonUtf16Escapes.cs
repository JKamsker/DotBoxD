using System.Buffers;
using System.Text.Json;

namespace DotBoxD.Queryable.Serialization;

internal static class QueryValueJsonUtf16Escapes
{
    public static void RejectMalformedEscapedUtf16(ref Utf8JsonReader reader, string name)
    {
        if (reader.HasValueSequence)
        {
            var value = reader.ValueSequence.ToArray();
            RejectMalformedEscapedUtf16(value, name);
            return;
        }

        RejectMalformedEscapedUtf16(reader.ValueSpan, name);
    }

    private static void RejectMalformedEscapedUtf16(ReadOnlySpan<byte> value, string name)
    {
        var index = 0;
        while (index < value.Length)
        {
            if (value[index] != (byte)'\\')
            {
                index++;
                continue;
            }

            index = AdvanceEscapedUtf16(value, name, index);
        }
    }

    private static int AdvanceEscapedUtf16(ReadOnlySpan<byte> value, string name, int backslashIndex)
    {
        var index = backslashIndex + 1;
        if (index >= value.Length)
        {
            return value.Length;
        }

        if (value[index] != (byte)'u')
        {
            return index + 1;
        }

        var codeUnit = ReadUnicodeEscape(value, index + 1);
        if (char.IsHighSurrogate((char)codeUnit))
        {
            return AdvanceSurrogatePair(value, name, index);
        }

        if (char.IsLowSurrogate((char)codeUnit))
        {
            throw EventQueryJsonStringSafety.MalformedUtf16(name);
        }

        return index + 5;
    }

    private static int AdvanceSurrogatePair(ReadOnlySpan<byte> value, string name, int unicodeEscapeIndex)
    {
        var nextEscape = unicodeEscapeIndex + 5;
        if (nextEscape + 5 >= value.Length ||
            value[nextEscape] != (byte)'\\' ||
            value[nextEscape + 1] != (byte)'u')
        {
            throw EventQueryJsonStringSafety.MalformedUtf16(name);
        }

        var nextCodeUnit = ReadUnicodeEscape(value, nextEscape + 2);
        if (!char.IsLowSurrogate((char)nextCodeUnit))
        {
            throw EventQueryJsonStringSafety.MalformedUtf16(name);
        }

        return nextEscape + 6;
    }

    private static int ReadUnicodeEscape(ReadOnlySpan<byte> value, int hexStart)
    {
        var codeUnit = 0;
        for (var i = 0; i < 4; i++)
        {
            var digit = HexValue(value[hexStart + i]);
            if (digit < 0)
            {
                throw new JsonException("Invalid Unicode escape in query value JSON.");
            }

            codeUnit = (codeUnit << 4) | digit;
        }

        return codeUnit;
    }

    private static int HexValue(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - (byte)'0',
        >= (byte)'A' and <= (byte)'F' => value - (byte)'A' + 10,
        >= (byte)'a' and <= (byte)'f' => value - (byte)'a' + 10,
        _ => -1,
    };
}
