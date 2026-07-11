namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.Contract.Effects;

public sealed class HostCapabilityEffectValidationContractTests
{
    [Fact]
    public void AddBindingsFrom_rejects_HostCapability_effects_with_unknown_bits()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IUnknownEffectProbeWorld>(new UnknownEffectProbeWorld()));

        Assert.Contains("Host capability", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IUnknownEffectProbeWorld.Read), ex.Message, StringComparison.Ordinal);
        Assert.Contains("unknown effects", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private interface IUnknownEffectProbeWorld
    {
        [HostCapability("probe.read.value", HostBindingEffect.HostStateRead | (HostBindingEffect)8)]
        int Read();
    }

    private sealed class UnknownEffectProbeWorld : IUnknownEffectProbeWorld
    {
        public int Read() => 1;
    }
}
