using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.Contract.Effects;

public sealed class HostBindingAttributeEffectValidationContractTests
{
    [Fact]
    public void Explicit_HostBindingAttribute_unknown_effect_bits_report_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(ExplicitHostBindingSource());

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("HostBinding", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("effect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Auto_HostBindingAttribute_unknown_effect_bits_report_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(AutoHostBindingSource());

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("HostBinding", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("effect", StringComparison.OrdinalIgnoreCase));
    }

    private static string ExplicitHostBindingSource()
        => """
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [RpcService]
            public interface IProbeWorld
            {
                [HostBinding(
                    "host.probe.read",
                    "probe.read.value",
                    SandboxEffect.Cpu | SandboxEffect.HostStateRead | (SandboxEffect)(1 << 30))]
                int Read(string id);
            }

            public sealed record ProbeEvent(string TargetId, string Message);

            [Plugin("unknown-host-binding-effect-explicit")]
            public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
            {
                public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                    => ctx.Host<IProbeWorld>().Read(e.TargetId) > 0;

                public void Handle(ProbeEvent e, HookContext ctx)
                {
                    ctx.Messages.Send(e.TargetId, e.Message);
                }
            }
            """;

    private static string AutoHostBindingSource()
        => """
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [RpcService]
            public interface IProbeWorld
            {
                [HostBinding(
                    "probe.read.value",
                    SandboxEffect.Cpu | SandboxEffect.HostStateRead | (SandboxEffect)(1 << 30))]
                int Read(string id);
            }

            public sealed record ProbeEvent(string TargetId, string Message);

            [Plugin("unknown-host-binding-effect-auto")]
            public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
            {
                public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                    => ctx.Host<IProbeWorld>().Read(e.TargetId) > 0;

                public void Handle(ProbeEvent e, HookContext ctx)
                {
                    ctx.Messages.Send(e.TargetId, e.Message);
                }
            }
            """;
}
