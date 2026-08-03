using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed class PluginAnalyzerKernelMethodEscapedNameofRegressionTests
{
    [Fact]
    public void Escaped_nameof_kernel_method_counts_non_repeatable_argument_use()
    {
        var source = """
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IProbeWorld
            {
                [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int GetValue(string id);
            }

            public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

            [Plugin("escaped-nameof-kernel-method")]
            public sealed partial class EscapedNameofKernel : IEventKernel<ProbeEvent>
            {
                public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                    => IsAtLeast(ctx.Host<IProbeWorld>().GetValue(e.TargetId), e.Threshold);

                public void Handle(ProbeEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);

                [KernelMethod]
                public static bool IsAtLeast(int value, int threshold) => @nameof(value) >= threshold;

                [KernelMethod]
                public static int @nameof(int input) => input;
            }
            """;

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("parameter 'value' is not used", StringComparison.Ordinal));

        var package = PluginAnalyzerGeneratedPackageFactory.Create(source, "Sample.EscapedNameofPluginPackage");

        Assert.Contains("probe.read.value", package.Manifest.RequiredCapabilities);
    }
}
