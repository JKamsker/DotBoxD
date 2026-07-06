using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// Renders a registered query into human-readable fact lines (event, fingerprint, index constraints,
/// projection shape, coverage) so a host can log exactly how it planned a dynamic subscription. The lines
/// are unadorned facts; a host adds its own prefix or sink.
/// </summary>
public static class EventQueryDiagnostics
{
    private static readonly IReadOnlyDictionary<QueryComparisonOperator, string> OperatorSymbols =
        new Dictionary<QueryComparisonOperator, string>
        {
            [QueryComparisonOperator.Equal] = "==",
            [QueryComparisonOperator.NotEqual] = "!=",
            [QueryComparisonOperator.GreaterThan] = ">",
            [QueryComparisonOperator.GreaterThanOrEqual] = ">=",
            [QueryComparisonOperator.LessThan] = "<",
            [QueryComparisonOperator.LessThanOrEqual] = "<=",
            [QueryComparisonOperator.StringContains] = "contains",
            [QueryComparisonOperator.StringStartsWith] = "startsWith",
            [QueryComparisonOperator.StringEndsWith] = "endsWith"
        };

    /// <summary>Produces the diagnostic fact lines for a subscription handle.</summary>
    public static IReadOnlyList<string> Describe(EventQuerySubscriptionHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var lines = new List<string>
        {
            $"query subscription registered: {ShortName(handle.Document.EventName)}",
            $"fingerprint: {handle.Fingerprint[..Math.Min(12, handle.Fingerprint.Length)]}",
        };

        foreach (var predicate in handle.Plan.IndexedPredicates)
        {
            lines.Add($"index: {predicate.Path} {Symbol(predicate.Operator)} {predicate.Value.ToCanonicalText()}");
        }

        if (handle.Plan.ResidualFilter is not null)
        {
            lines.Add("residual: present");
        }

        lines.Add($"projection: {DescribeProjection(handle.Document.Projection)}");
        lines.Add($"coverage: {handle.Plan.Coverage.ToString().ToLowerInvariant()}");
        return lines;
    }

    /// <summary>Produces the diagnostic lines as a single newline-joined string.</summary>
    public static string DescribeText(EventQuerySubscriptionHandle handle)
        => string.Join(Environment.NewLine, Describe(handle));

    private static string DescribeProjection(QueryProjection projection) => projection.Kind switch
    {
        QueryProjectionKind.Identity => "(event)",
        QueryProjectionKind.Member => projection.Path ?? "(member)",
        QueryProjectionKind.Construct =>
            $"{ShortName(projection.TypeName)} {{ {string.Join(", ", projection.Fields.Select(f => f.Name))} }}",
        _ => projection.Kind.ToString(),
    };

    private static string Symbol(QueryComparisonOperator op)
        => OperatorSymbols.TryGetValue(op, out var symbol) ? symbol : op.ToString();

    private static string ShortName(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
        {
            return "(unknown)";
        }

        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < fullName.Length - 1 ? fullName[(lastDot + 1)..] : fullName;
    }
}
