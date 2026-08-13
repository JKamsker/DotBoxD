using System.Security.Cryptography;
using System.Text;

namespace DotBoxD.Kernels.Debugging;

/// <summary>A client-side source document identified by normalized path and SHA-256 checksum.</summary>
public sealed record KernelDebugDocument
{
    public KernelDebugDocument(string id, string path, string sha256Checksum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Path = NormalizePath(path);
        Sha256Checksum = ValidateChecksum(sha256Checksum);
    }

    public string Id { get; }

    public string Path { get; }

    public string Sha256Checksum { get; }

    public static KernelDebugDocument FromSource(string id, string path, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return FromSourceBytes(id, path, Encoding.UTF8.GetBytes(source));
    }

    public static KernelDebugDocument FromSourceBytes(string id, string path, ReadOnlySpan<byte> source)
        => new(id, path, Convert.ToHexStringLower(SHA256.HashData(source)));

    public bool MatchesSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return MatchesSourceBytes(Encoding.UTF8.GetBytes(source));
    }

    public bool MatchesSourceBytes(ReadOnlySpan<byte> source)
        => string.Equals(
            Sha256Checksum,
            Convert.ToHexStringLower(SHA256.HashData(source)),
            StringComparison.Ordinal);

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/');
        var schemeSeparator = normalized.IndexOf("://", StringComparison.Ordinal);
        var isUncPath = schemeSeparator < 0 && normalized.StartsWith("//", StringComparison.Ordinal);
        var prefixLength = isUncPath ? 2 : schemeSeparator < 0 ? 0 : schemeSeparator + 3;
        var prefix = normalized[..prefixLength];
        var remainder = isUncPath
            ? normalized[prefixLength..].TrimStart('/')
            : normalized[prefixLength..];
        while (remainder.Contains("//", StringComparison.Ordinal))
        {
            remainder = remainder.Replace("//", "/", StringComparison.Ordinal);
        }

        return prefix + remainder;
    }

    private static string ValidateChecksum(string checksum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checksum);
        if (checksum.Length != 64 || checksum.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("Source checksums must be lowercase SHA-256 hex strings.", nameof(checksum));
        }

        return checksum;
    }
}
