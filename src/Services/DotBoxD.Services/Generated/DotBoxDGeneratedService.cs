namespace DotBoxD.Services.Generated;

/// <summary>
/// Describes a source-generated DotBoxD service and its generated implementation types.
/// </summary>
public readonly record struct DotBoxDGeneratedService(
    Type ServiceType,
    Type ProxyType,
    Type DispatcherType,
    string ServiceName)
{
    /// <summary>
    /// Describes the RPC-facing methods generated for this service.
    /// </summary>
    public IReadOnlyList<DotBoxDGeneratedMethod> Methods { get; init; } = Array.Empty<DotBoxDGeneratedMethod>();

    /// <summary>
    /// Creates service metadata with generated method descriptors.
    /// </summary>
    public DotBoxDGeneratedService(
        Type serviceType,
        Type proxyType,
        Type dispatcherType,
        string serviceName,
        IReadOnlyList<DotBoxDGeneratedMethod> methods)
        : this(serviceType, proxyType, dispatcherType, serviceName)
    {
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
    }
}
