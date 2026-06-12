using System.Text;
using System.Threading;

namespace ShaRPC.Serializers.MessagePack;

internal static class RpcStringCache
{
    private const int MaxEntries = 128;
    private const int MaxUtf8Bytes = 256;
    private const int MaxChars = 256;

    private static readonly object Gate = new();
    private static readonly Entry?[] Entries = new Entry?[MaxEntries];
    private static int _count;
    private static int _nextReplacement;

    public static string GetOrAdd(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaxUtf8Bytes)
        {
            return Encoding.UTF8.GetString(utf8);
        }

        var count = Volatile.Read(ref _count);
        for (var i = 0; i < count; i++)
        {
            var entry = Volatile.Read(ref Entries[i]);
            if (entry is not null && utf8.SequenceEqual(entry.Utf8))
            {
                return entry.Value;
            }
        }

        return GetOrAdd(Encoding.UTF8.GetString(utf8));
    }

    public static string GetOrAdd(string value)
    {
        if (value.Length > MaxChars)
        {
            return value;
        }

        var count = Volatile.Read(ref _count);
        for (var i = 0; i < count; i++)
        {
            var entry = Volatile.Read(ref Entries[i]);
            if (entry is not null && string.Equals(value, entry.Value, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        lock (Gate)
        {
            count = Volatile.Read(ref _count);
            for (var i = 0; i < count; i++)
            {
                var existing = Volatile.Read(ref Entries[i]);
                if (existing is not null && string.Equals(value, existing.Value, StringComparison.Ordinal))
                {
                    return existing.Value;
                }
            }

            var entry = new Entry(value, Encoding.UTF8.GetBytes(value));
            if (count < MaxEntries)
            {
                Volatile.Write(ref Entries[count], entry);
                Volatile.Write(ref _count, count + 1);
            }
            else
            {
                var index = _nextReplacement;
                _nextReplacement = (index + 1) % MaxEntries;
                Volatile.Write(ref Entries[index], entry);
            }

            return value;
        }
    }

    private sealed class Entry
    {
        public Entry(string value, byte[] utf8)
        {
            Value = value;
            Utf8 = utf8;
        }

        public string Value { get; }

        public byte[] Utf8 { get; }
    }
}
