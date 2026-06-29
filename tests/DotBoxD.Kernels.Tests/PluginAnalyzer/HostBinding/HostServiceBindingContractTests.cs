namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceBindingContractTests
{
    [Fact]
    public void AddBindingsFrom_rejects_concrete_service_contracts()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<ConcreteProbeWorld>(new ConcreteProbeWorld()));

        Assert.Contains("must be an interface", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddBindingsFrom_rejects_overloaded_methods_that_share_a_binding_route()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IOverloadedProbeWorld>(new OverloadedProbeWorld()));

        Assert.Contains("duplicate host binding route", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Read", ex.Message, StringComparison.Ordinal);
    }

    private sealed class ConcreteProbeWorld
    {
        [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
        public int Read() => 1;
    }

    private interface IOverloadedProbeWorld
    {
        [HostCapability("probe.read.text", HostBindingEffect.HostStateRead)]
        int Read(string id);

        [HostCapability("probe.read.number", HostBindingEffect.HostStateRead)]
        int Read(int id);
    }

    private sealed class OverloadedProbeWorld : IOverloadedProbeWorld
    {
        public int Read(string id) => id.Length;

        public int Read(int id) => id;
    }
}
