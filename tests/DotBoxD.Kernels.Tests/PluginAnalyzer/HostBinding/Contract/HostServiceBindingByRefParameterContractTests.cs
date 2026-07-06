using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceBindingByRefParameterContractTests
{
    [Theory]
    [MemberData(nameof(ByRefHostBindingCases))]
    public void AddBindingsFrom_rejects_by_ref_HostBinding_parameters(
        Action<SandboxHostBuilder> configure,
        string memberName,
        string parameterShape)
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(() => configure(builder));

        Assert.Contains("HostBinding", ex.Message, StringComparison.Ordinal);
        Assert.Contains(memberName, ex.Message, StringComparison.Ordinal);
        Assert.Contains($"{parameterShape} parameter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<Action<SandboxHostBuilder>, string, string> ByRefHostBindingCases()
        => new()
        {
            {
                builder => builder.AddBindingsFrom<IRefProbeWorld>(new RefProbeWorld()),
                nameof(IRefProbeWorld.Read),
                "ref"
            },
            {
                builder => builder.AddBindingsFrom<IOutProbeWorld>(new OutProbeWorld()),
                nameof(IOutProbeWorld.Read),
                "out"
            },
            {
                builder => builder.AddBindingsFrom<IInProbeWorld>(new InProbeWorld()),
                nameof(IInProbeWorld.Read),
                "in"
            }
        };

    private interface IRefProbeWorld
    {
        [HostBinding("host.probe.ref", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(ref int value);
    }

    private sealed class RefProbeWorld : IRefProbeWorld
    {
        public int Read(ref int value) => value;
    }

    private interface IOutProbeWorld
    {
        [HostBinding("host.probe.out", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(out int value);
    }

    private sealed class OutProbeWorld : IOutProbeWorld
    {
        public int Read(out int value)
        {
            value = 1;
            return value;
        }
    }

    private interface IInProbeWorld
    {
        [HostBinding("host.probe.in", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(in int value);
    }

    private sealed class InProbeWorld : IInProbeWorld
    {
        public int Read(in int value) => value;
    }
}
