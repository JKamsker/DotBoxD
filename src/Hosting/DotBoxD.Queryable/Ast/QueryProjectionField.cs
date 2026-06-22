namespace DotBoxD.Queryable.Ast;

/// <summary>
/// One member of a <see cref="QueryProjectionKind.Construct"/> projection. A field is either a dotted
/// member read from the source event (<see cref="Path"/> set) or a captured constant
/// (<see cref="Constant"/> set). <see cref="Name"/> is the output member name (constructor parameter or
/// property), preserved so a host can describe the projected payload.
/// </summary>
public sealed record QueryProjectionField
{
    /// <summary>The output member name (for example the DTO constructor parameter name).</summary>
    public required string Name { get; init; }

    /// <summary>The dotted source member path when this field reads from the event; otherwise <see langword="null"/>.</summary>
    public string? Path { get; init; }

    /// <summary>The captured constant when this field is a literal; otherwise <see langword="null"/>.</summary>
    public QueryValue? Constant { get; init; }

    /// <summary>Builds a field that reads a member from the source event.</summary>
    public static QueryProjectionField FromMember(string name, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new QueryProjectionField { Name = name, Path = path };
    }

    /// <summary>Builds a field that emits a captured constant.</summary>
    public static QueryProjectionField FromConstant(string name, QueryValue constant)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(constant);
        return new QueryProjectionField { Name = name, Constant = constant };
    }
}
