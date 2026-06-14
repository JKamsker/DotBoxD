namespace DotBoxD.Services.Generated;

/// <summary>
/// Describes a source-generated DotBoxD service and its generated implementation types.
/// </summary>
public readonly record struct GeneratedService(
    Type ServiceType,
    Type ProxyType,
    Type DispatcherType,
    string ServiceName)
{
    /// <summary>
    /// Describes the RPC-facing methods generated for this service.
    /// </summary>
    public IReadOnlyList<GeneratedMethod> Methods { get; init; } = Array.Empty<GeneratedMethod>();

    /// <summary>
    /// Creates service metadata with generated method descriptors.
    /// </summary>
    public GeneratedService(
        Type serviceType,
        Type proxyType,
        Type dispatcherType,
        string serviceName,
        IReadOnlyList<GeneratedMethod> methods)
        : this(serviceType, proxyType, dispatcherType, serviceName)
    {
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
    }
}
