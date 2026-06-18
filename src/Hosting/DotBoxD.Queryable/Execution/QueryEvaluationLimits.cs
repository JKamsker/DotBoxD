namespace DotBoxD.Queryable.Execution;

/// <summary>
/// Bounds applied when interpreting a portable filter so a maliciously deep or wide AST (for example one
/// arriving from an untrusted serialized source) cannot exhaust the stack or stall the dispatch path.
/// </summary>
public static class QueryEvaluationLimits
{
    /// <summary>The maximum nesting depth of a filter tree.</summary>
    public const int MaxDepth = 32;

    /// <summary>The maximum total number of nodes (connectives plus leaves) in a filter tree.</summary>
    public const int MaxNodes = 256;
}
