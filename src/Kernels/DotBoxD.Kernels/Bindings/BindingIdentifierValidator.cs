namespace DotBoxD.Kernels.Bindings;

internal static class BindingIdentifierValidator
{
    private static readonly HashSet<string> ForbiddenClrRoots = new(StringComparer.OrdinalIgnoreCase) {
        "System", "Microsoft", "Assembly", "Type", "Reflection", "Process",
        "Environment", "Thread", "Task", "DllImport", "IServiceProvider"
    };

    internal static bool TryValidate(string? value, out string? message)
    {
        if (string.IsNullOrWhiteSpace(value) || ContainsControlCharacter(value))
        {
            message = "must be non-empty and must not contain control characters";
            return false;
        }

        if (!HasDottedIdentifierGrammar(value))
        {
            message = $"'{value}' must be a dot-separated identifier";
            return false;
        }

        if (LooksLikeRawClrReference(value))
        {
            message = $"'{value}' looks like a forbidden CLR reference";
            return false;
        }

        message = null;
        return true;
    }

    private static bool HasDottedIdentifierGrammar(string value)
    {
        var expectingSegmentStart = true;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '.')
            {
                if (expectingSegmentStart)
                {
                    return false;
                }

                expectingSegmentStart = true;
                continue;
            }

            if (expectingSegmentStart)
            {
                if (!IsIdentifierStart(ch))
                {
                    return false;
                }

                expectingSegmentStart = false;
                continue;
            }

            if (!IsIdentifierPart(ch))
            {
                return false;
            }
        }

        return !expectingSegmentStart;
    }

    private static bool LooksLikeRawClrReference(string value)
    {
        var dot = value.IndexOf('.');
        var firstSegment = dot < 0 ? value : value[..dot];
        var rootSegment = string.Equals(firstSegment, "host", StringComparison.OrdinalIgnoreCase) && dot >= 0
            ? NextSegment(value, dot + 1)
            : firstSegment;
        return ForbiddenClrRoots.Contains(rootSegment);
    }

    private static string NextSegment(string value, int start)
    {
        var dot = value.IndexOf('.', start);
        return dot < 0 ? value[start..] : value[start..dot];
    }

    private static bool ContainsControlCharacter(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifierStart(char ch)
        => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    private static bool IsIdentifierPart(char ch)
        => IsIdentifierStart(ch) || ch is >= '0' and <= '9';
}
