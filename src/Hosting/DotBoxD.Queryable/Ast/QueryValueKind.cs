namespace DotBoxD.Queryable.Ast;

/// <summary>
/// The runtime category of a <see cref="QueryValue"/>. The portable model keeps a small, closed set of
/// scalar kinds so a host can interpret a captured query without binding to CLR type identities.
/// </summary>
public enum QueryValueKind
{
    /// <summary>The <c>null</c> literal.</summary>
    Null = 0,

    /// <summary>A boolean literal.</summary>
    Boolean = 1,

    /// <summary>A signed integral literal carried as an exact 64-bit value (covers <c>sbyte</c>…<c>long</c>).</summary>
    Integer = 2,

    /// <summary>A floating-point literal carried as a 64-bit double (covers <c>float</c> and <c>double</c>).</summary>
    Number = 3,

    /// <summary>A string literal.</summary>
    String = 4,

    /// <summary>A <see cref="System.Guid"/> literal (equality only; no ordering).</summary>
    Guid = 5,

    /// <summary>An exact <see cref="System.Decimal"/> literal carried losslessly (scale-insensitive equality).</summary>
    Decimal = 6,

    /// <summary>An unsigned 64-bit integral literal carried as an exact <c>ulong</c> (full range, no double collapse).</summary>
    UnsignedInteger = 7,

    /// <summary>
    /// A timestamp literal carried as a UTC-normalized instant (covers <c>DateTime</c>/<c>DateTimeOffset</c>/<c>DateOnly</c>).
    /// </summary>
    Timestamp = 8,
}
