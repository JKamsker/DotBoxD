namespace DotBoxD.Services.Generated;

/// <summary>
/// Describes a source-generated DotBoxD service method.
/// </summary>
public readonly record struct GeneratedMethod(
    string Name,
    string WireName,
    Type ReturnType,
    Type? ResultType,
    GeneratedReturnKind ReturnKind,
    bool ReturnsNestedService,
    IReadOnlyList<GeneratedParameter> Parameters);
