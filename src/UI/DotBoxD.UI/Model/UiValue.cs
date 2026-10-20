using System.Collections.Immutable;

namespace DotBoxD.UI;

public enum UiValueKind
{
    Boolean = 1,
    Int32 = 2,
    Number = 3,
    String = 4,
    Items = 5
}

/// <summary>Closed scalar/keyed-text-list union. Unused fields must retain their defaults.</summary>
public sealed record UiValue(
    UiValueKind Kind,
    bool Boolean = false,
    int Integer = 0,
    double Number = 0,
    string Text = "")
{
    public ImmutableArray<UiListItem> Items { get; init; } = [];

    public bool Equals(UiValue? other)
        => other is not null && Kind == other.Kind && Boolean == other.Boolean && Integer == other.Integer &&
            Number.Equals(other.Number) && string.Equals(Text, other.Text, StringComparison.Ordinal) && EqualItems(Items, other.Items);

    private static bool EqualItems(ImmutableArray<UiListItem> left, ImmutableArray<UiListItem> right)
        => left.IsDefault || right.IsDefault ? left.IsDefault == right.IsDefault : left.AsSpan().SequenceEqual(right.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Boolean);
        hash.Add(Integer);
        hash.Add(Number);
        hash.Add(Text, StringComparer.Ordinal);
        if (!Items.IsDefault)
        { foreach (var item in Items) { hash.Add(item); } }
        return hash.ToHashCode();
    }

    public static UiValue FromItems(ImmutableArray<UiListItem> items) => new(UiValueKind.Items) { Items = items };
    public static UiValue FromBoolean(bool value) => new(UiValueKind.Boolean, Boolean: value);
    public static UiValue FromInt32(int value) => new(UiValueKind.Int32, Integer: value);
    public static UiValue FromNumber(double value) => new(UiValueKind.Number, Number: value);
    public static UiValue FromString(string value) => new(UiValueKind.String, Text: value);
}

/// <summary>Bounded data-only row; a stable key preserves the trusted renderer row identity.</summary>
public sealed record UiListItem(string Key, string Text);
