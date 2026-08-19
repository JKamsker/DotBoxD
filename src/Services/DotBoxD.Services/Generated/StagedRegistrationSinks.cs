using DotBoxD.Services.Server;

namespace DotBoxD.Services.Generated;

internal sealed class StagedServiceRegistrationSink : IRpcServiceRegistrationSink
{
    private readonly List<Action<IRpcServiceRegistrationSink>> _registrations = new();

    public void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService =>
        _registrations.Add(sink => sink.AddService<TService, TImplementation>());

    public void CommitTo(IRpcServiceRegistrationSink sink)
    {
        foreach (var registration in _registrations)
        {
            registration(sink);
        }
    }
}

internal sealed class StagedGeneratedServiceRegistrationSink : IRpcGeneratedServiceRegistrationSink
{
    private readonly List<Action<IRpcGeneratedServiceRegistrationSink>> _registrations = new();

    public void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher =>
        _registrations.Add(sink => sink.AddService<TService, TProxy, TDispatcher>());

    public void CommitTo(IRpcGeneratedServiceRegistrationSink sink)
    {
        foreach (var registration in _registrations)
        {
            registration(sink);
        }
    }
}
