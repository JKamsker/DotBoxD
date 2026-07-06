using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceExplicitMethodBindingTests
{
    private const string ExplicitMethodBindingSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

        // This plugin-local contract intentionally differs from the host contract below. Both share
        // the same HostBinding id to prove binding resolution is keyed by id, not CLR type identity.
        [RpcService]
        public interface IProbeWorld
        {
            [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);
        }

        public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

        [Plugin("explicit-method-host-binding-probe")]
        public sealed partial class ExplicitMethodBindingKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().GetValue(e.TargetId) >= e.Threshold;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    [Fact]
    public async Task AddBindingsFrom_registers_explicit_method_host_binding_ids()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            ExplicitMethodBindingSource,
            "DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.ExplicitMethodBindingPluginPackage");
        using var server = PluginServer.Create(
            configureHost: builder => builder.AddBindingsFrom<IExplicitMethodProbeWorld>(new ExplicitMethodProbeWorld()),
            defaultPolicy: PluginAnalyzerHostBindingTestSupport.ProbeReadPolicy());

        var kernel = await server.InstallAsync(package);
        var adapter = new PluginAnalyzerHostBindingTestSupport.ProbeEventAdapter();
        var probe = new PluginAnalyzerHostBindingTestSupport.ProbeEvent("monster-42", "matched", 40);

        Assert.True(await kernel.ShouldHandleAsync(adapter, probe));
    }

    // This host contract mirrors the plugin source binding id while remaining a separate CLR type.
    private interface IExplicitMethodProbeWorld
    {
        [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
        [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int GetValue(string id);
    }

    private sealed class ExplicitMethodProbeWorld : IExplicitMethodProbeWorld
    {
        public int GetValue(string id) => id == "monster-42" ? 42 : 0;
    }
}
