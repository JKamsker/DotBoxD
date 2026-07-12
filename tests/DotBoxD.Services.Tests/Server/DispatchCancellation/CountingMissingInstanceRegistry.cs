using DotBoxD.Services.Server;

namespace DotBoxD.Services.Tests.Server.DispatchCancellation;

internal sealed class CountingMissingInstanceRegistry : IInstanceRegistry
{
    public int TryGetCalls { get; private set; }

    public string Register(string serviceName, object instance) =>
        throw new NotSupportedException();

    public bool TryGet(string serviceName, string instanceId, out object instance)
    {
        TryGetCalls++;
        instance = null!;
        return false;
    }

    public void Release(string serviceName, string instanceId) =>
        throw new NotSupportedException();

    public ValueTask ReleaseAsync(string serviceName, string instanceId) =>
        throw new NotSupportedException();

    public void ReleaseAll() =>
        throw new NotSupportedException();
}
