namespace DotBoxD.Services.Generated;

/// <summary>
/// Receives source-generated service registrations without scanning generated types.
/// </summary>
public interface IDotBoxDServiceRegistrationSink
{
    /// <summary>
    /// Adds one generated proxy implementation for a DotBoxD service interface.
    /// </summary>
    void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService;
}
