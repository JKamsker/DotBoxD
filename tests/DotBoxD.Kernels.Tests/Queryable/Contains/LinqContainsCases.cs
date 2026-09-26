namespace DotBoxD.Kernels.Tests.Queryable;

internal static class LinqContainsCases
{
    public static readonly string[] ForwardingOperators =
    [
        "distinct", "append", "prepend", "append-many", "prepend-many",
        "union-left", "union-right", "union-many", "concat-left", "concat-right", "concat-many",
        "reverse", "order", "order-descending", "then-by", "shuffle", "default-if-empty",
        "nested", "select-many", "select-many-second", "shuffle-take-all", "shared-source"
    ];

    public static IEnumerable<string> Forward(string kind, IEnumerable<string> source) => kind switch
    {
        "distinct" => source.Distinct(),
        "append" => source.Append("tail"),
        "prepend" => source.Prepend("head"),
        "append-many" => source.Append("tail").Append("end"),
        "prepend-many" => source.Prepend("head").Prepend("start"),
        "union-left" => source.Union(new[] { "other" }),
        "union-right" => new[] { "other" }.Union(source),
        "union-many" => new[] { "other" }.Union(new[] { "next" }).Union(source),
        "concat-left" => source.Concat(new[] { "other" }),
        "concat-right" => new[] { "other" }.Concat(source),
        "concat-many" => new[] { "other" }.Concat(new[] { "next" }).Concat(source),
        "reverse" => source.Reverse(),
        "order" => source.OrderBy(x => x, StringComparer.Ordinal),
        "order-descending" => source.OrderDescending(StringComparer.Ordinal),
        "then-by" => source.OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal),
        "shuffle" => source.Shuffle(),
        "default-if-empty" => source.DefaultIfEmpty("empty"),
        "nested" => source.Reverse().Append("tail").Distinct().Concat(new[] { "other" }),
        "select-many" => new[] { 1 }.SelectMany(_ => source),
        "select-many-second" => new[] { 0, 1 }.SelectMany(i => i == 0 ? new[] { "other" } : source),
        "shuffle-take-all" => ShuffleAfterShrink(source),
        "shared-source" => source.Concat(source),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static IEnumerable<string> ShuffleAfterShrink(IEnumerable<string> source)
    {
        var padding = Enumerable.Repeat("padding", 20).ToList();
        var values = source.Concat(padding).Shuffle().Take(10);
        padding.Clear();
        return values;
    }

    public sealed class CustomList() : List<string>(["alice"]), ICollection<string>
    {
        bool ICollection<string>.Contains(string item) => item == "ALICE";
    }
}
