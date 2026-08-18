using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventAdapterResolveConcurrencyTests
{
    [Fact]
    public async Task Resolve_returns_the_registered_adapter_when_concurrent_discovery_creates_multiple_instances()
    {
        var registry = new PluginEventAdapterRegistry();

        var resolutions = await Task.WhenAll(
            Task.Run(registry.Resolve<ConcurrentDiscoveryEvent>),
            Task.Run(registry.Resolve<ConcurrentDiscoveryEvent>));
        var laterResolution = registry.Resolve<ConcurrentDiscoveryEvent>();

        Assert.Same(resolutions[0], resolutions[1]);
        Assert.Same(resolutions[0], laterResolution);
    }

    public sealed record ConcurrentDiscoveryEvent;

    public sealed class ConcurrentDiscoveryEventAdapter : IPluginEventAdapter<ConcurrentDiscoveryEvent>
    {
        private static readonly Barrier ConstructorRendezvous = new(2);

        public ConcurrentDiscoveryEventAdapter()
        {
            if (!ConstructorRendezvous.SignalAndWait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Concurrent Resolve calls did not both reach adapter discovery.");
            }
        }

        public string EventName => nameof(ConcurrentDiscoveryEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ConcurrentDiscoveryEvent e) => [];
    }
}
