using System.Text;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class MessagePackUtf16ProbeValidators
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void LegacyValidate(string value)
    {
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

                throw new InvalidOperationException("The valid benchmark value contained a lone high surrogate.");
            }

            if (char.IsLowSurrogate(current))
            {
                throw new InvalidOperationException("The valid benchmark value contained a lone low surrogate.");
            }
        }
    }

    public static void PrefilterValidate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (!char.IsSurrogate(current))
            {
                continue;
            }

            if (char.IsHighSurrogate(current) &&
                i + 1 < value.Length &&
                char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            throw new InvalidOperationException("The valid benchmark value contained a malformed surrogate.");
        }
    }

    public static void StrictValidate(string value)
        => _ = StrictUtf8.GetByteCount(value);
}
