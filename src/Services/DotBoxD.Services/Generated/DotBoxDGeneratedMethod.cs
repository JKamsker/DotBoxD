namespace DotBoxD.Services.Generated;

/// <summary>
/// Describes a source-generated DotBoxD service method.
/// </summary>
public readonly record struct DotBoxDGeneratedMethod(
    string Name,
    string WireName,
    Type ReturnType,
    Type? ResultType,
    DotBoxDGeneratedReturnKind ReturnKind,
    bool ReturnsNestedService,
    IReadOnlyList<DotBoxDGeneratedParameter> Parameters);
