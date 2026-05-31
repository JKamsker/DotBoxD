namespace ShaRPC.Core.Generated;

/// <summary>
/// Describes a source-generated ShaRPC service and its generated implementation types.
/// </summary>
public readonly record struct ShaRpcGeneratedService(
    Type ServiceType,
    Type ProxyType,
    Type DispatcherType,
    string ServiceName);
