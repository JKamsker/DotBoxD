using System.Globalization;

namespace DotBoxD.Kernels.Debugging;

/// <summary>Identifies one structural node in sandbox IR without depending on source or package identity.</summary>
public sealed record SandboxNodeId
{
    public const int CurrentVersion = 1;

    private readonly string _value;

    public SandboxNodeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Version = ParseVersion(value);
        ValidateDigest(value);
        _value = value;
    }

    public string Value => _value;

    public int Version { get; }

    public override string ToString() => Value;

    private static int ParseVersion(string value)
    {
        if (value.Length < 4 || value[0] != 'v')
        {
            throw Invalid(value);
        }

        var separator = value.IndexOf(':');
        if (separator < 2 ||
            !int.TryParse(value.AsSpan(1, separator - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version <= 0)
        {
            throw Invalid(value);
        }

        return version;
    }

    private static void ValidateDigest(string value)
    {
        var separator = value.IndexOf(':');
        var digest = value.AsSpan(separator + 1);
        if (digest.Length != 64)
        {
            throw Invalid(value);
        }

        foreach (var character in digest)
        {
            if (!IsLowerHex(character))
            {
                throw Invalid(value);
            }
        }
    }

    private static bool IsLowerHex(char value)
        => value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static ArgumentException Invalid(string value)
        => new($"'{value}' is not a versioned sandbox node ID.", nameof(value));
}
