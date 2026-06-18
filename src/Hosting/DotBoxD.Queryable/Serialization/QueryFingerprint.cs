using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// Computes a stable, process-independent fingerprint for a query. The fingerprint is the SHA-256 of the
/// canonicalized JSON document, so structurally-equivalent queries (including reordered
/// <see cref="QueryFilterKind.And"/>/<see cref="QueryFilterKind.Or"/> operands) share a fingerprint. Use
/// it as a cache key, a log correlation id, or a replay signature.
/// </summary>
public static class QueryFingerprint
{
    /// <summary>Computes the full lowercase hex SHA-256 fingerprint of a document.</summary>
    public static string Compute(EventQueryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var canonical = EventQueryCanonicalizer.Canonicalize(document);
        var json = EventQueryJson.Serialize(canonical);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes a short fingerprint token (the first 12 hex characters) suitable for compact log lines.
    /// </summary>
    public static string ComputeShort(EventQueryDocument document)
        => Compute(document)[..12];

    /// <summary>Serializes a filter subtree to its canonical JSON text (used as a deterministic sort key).</summary>
    internal static string CanonicalText(QueryFilter filter)
        => JsonSerializer.Serialize(filter, EventQueryJson.Options);
}
