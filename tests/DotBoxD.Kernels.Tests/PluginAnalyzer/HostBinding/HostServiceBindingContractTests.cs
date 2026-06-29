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

    private sealed class ConcreteProbeWorld
    {
        [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
        public int Read() => 1;
    }
}
