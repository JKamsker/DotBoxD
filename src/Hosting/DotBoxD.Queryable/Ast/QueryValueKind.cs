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

    /// <summary>A signed integral literal carried as a 64-bit value (covers <c>byte</c>…<c>long</c>).</summary>
    Integer = 2,

    /// <summary>A floating-point literal carried as a 64-bit double (covers <c>float</c>, <c>double</c>, <c>decimal</c>).</summary>
    Number = 3,

    /// <summary>A string literal.</summary>
    String = 4,
}
