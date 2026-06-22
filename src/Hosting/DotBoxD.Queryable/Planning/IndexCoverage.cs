namespace DotBoxD.Queryable.Planning;

/// <summary>Describes how much of a query's filter a host can satisfy from index constraints alone.</summary>
public enum IndexCoverage
{
    /// <summary>No part of the filter is index-covered; the whole filter is a residual scan.</summary>
    None = 0,

    /// <summary>Some predicates are index-covered; a residual filter must still be evaluated on candidates.</summary>
    Partial = 1,

    /// <summary>Every predicate is index-covered; no residual evaluation is required for correctness.</summary>
    Full = 2,
}
