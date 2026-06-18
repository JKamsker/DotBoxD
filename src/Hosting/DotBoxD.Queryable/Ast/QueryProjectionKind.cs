using System.Text.Json.Serialization;

namespace DotBoxD.Queryable.Ast;

/// <summary>The shape of a portable <see cref="QueryProjection"/>.</summary>
public enum QueryProjectionKind
{
    /// <summary>Identity projection: the event itself flows through unchanged.</summary>
    [JsonStringEnumMemberName("identity")]
    Identity = 0,

    /// <summary>A single dotted member read (for example <c>e.AttackerId</c> or <c>e.Source.Id</c>).</summary>
    [JsonStringEnumMemberName("member")]
    Member = 1,

    /// <summary>Construction of a DTO/anonymous payload from member reads and constants.</summary>
    [JsonStringEnumMemberName("construct")]
    Construct = 2,
}
