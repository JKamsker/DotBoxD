namespace DotBoxD.Services.Generated;

/// <summary>
/// Describes a source-generated DotBoxD service method parameter.
/// </summary>
public readonly record struct GeneratedParameter(
    string Name,
    Type Type,
    int Position,
    bool IsCancellationToken,
    bool HasDefaultValue,
    object? DefaultValue);
