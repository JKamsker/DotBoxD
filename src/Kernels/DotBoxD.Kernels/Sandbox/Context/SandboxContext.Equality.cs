using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext
{
    /// <summary>
    /// Evaluates kernel <c>==</c> structural equality, first charging fuel proportional to the
    /// comparison work (see <see cref="SandboxEqualityFuel"/>). Scalars charge nothing extra, so only
    /// text and collection comparisons cost more than the operator's flat expression fuel. Both the
    /// interpreter and the compiled tier route boxed <c>==</c>/<c>!=</c> through here so the tiers
    /// charge identical fuel.
    /// </summary>
    internal bool ChargedValueEquals(SandboxValue left, SandboxValue right)
    {
        var cost = SandboxEqualityFuel.Measure(left, right);
        if (cost > 0)
        {
            ChargeFuel(cost);
        }

        return Equals(left, right);
    }
}

/// <summary>
/// Deterministic upper bound of the element and character comparisons structural equality performs:
/// one unit per compared collection element/record field/map entry, plus one per character when two
/// equal-length texts are compared ordinally (mirroring <c>string.equals</c>' per-character charge).
/// Comparisons <see cref="object.Equals(object?, object?)"/> rejects in O(1) (different kinds, item
/// types or counts, different text lengths) cost nothing.
/// </summary>
internal static class SandboxEqualityFuel
{
    public static long Measure(SandboxValue left, SandboxValue right)
        => (left, right) switch
        {
            (StringValue l, StringValue r) => Text(l.Value, r.Value),
            (OpaqueIdValue l, OpaqueIdValue r) => Text(l.Value, r.Value),
            (SandboxPathValue l, SandboxPathValue r) => Text(l.Value.RelativePath, r.Value.RelativePath),
            (SandboxUriValue l, SandboxUriValue r) => Text(l.Value.Value, r.Value.Value),
            (ListValue l, ListValue r) => List(l, r),
            (RecordValue l, RecordValue r) => Pairwise(l.Fields, r.Fields),
            (MapValue l, MapValue r) => Map(l, r),
            _ => 0
        };

    private static long Text(string left, string right)
        => left.Length == right.Length ? left.Length : 0;

    private static long List(ListValue left, ListValue right)
        => left.ItemType.Equals(right.ItemType) ? Pairwise(left.Values, right.Values) : 0;

    private static long Pairwise(IReadOnlyList<SandboxValue> left, IReadOnlyList<SandboxValue> right)
    {
        if (left.Count != right.Count)
        {
            return 0;
        }

        long total = left.Count;
        for (var i = 0; i < left.Count; i++)
        {
            total += Measure(left[i], right[i]);
        }

        return total;
    }

    private static long Map(MapValue left, MapValue right)
    {
        var entries = left.Values;
        var others = right.Values;
        if (!left.KeyType.Equals(right.KeyType) ||
            !left.ValueType.Equals(right.ValueType) ||
            entries.Count != others.Count)
        {
            return 0;
        }

        long total = entries.Count;
        foreach (var entry in entries)
        {
            // The key lookup hashes the key, which is proportional to its text length.
            total += KeyHash(entry.Key);
            if (others.TryGetValue(entry.Key, out var other))
            {
                total += Measure(entry.Value, other);
            }
        }

        return total;
    }

    private static long KeyHash(SandboxValue key)
        => key switch
        {
            StringValue text => text.Value.Length,
            OpaqueIdValue id => id.Value.Length,
            _ => 0
        };
}
