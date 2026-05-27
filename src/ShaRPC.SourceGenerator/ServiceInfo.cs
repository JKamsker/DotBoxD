namespace ShaRPC.SourceGenerator;

/// <summary>
/// Immutable, value-equatable representation of a ShaRPC service.
/// </summary>
internal sealed record ServiceModel(
    string Namespace,
    string InterfaceName,
    string ServiceName,
    EquatableArray<MethodModel> Methods);

/// <summary>
/// Immutable, value-equatable representation of a service method.
/// </summary>
internal sealed record MethodModel(
    string Name,
    string RpcName,
    string ReturnType,
    string? UnwrappedReturnType,
    bool ReturnsTask,
    bool ReturnsVoid,
    EquatableArray<ParameterModel> Parameters);

/// <summary>
/// Immutable, value-equatable representation of a method parameter.
/// </summary>
internal sealed record ParameterModel(string Name, string Type);
