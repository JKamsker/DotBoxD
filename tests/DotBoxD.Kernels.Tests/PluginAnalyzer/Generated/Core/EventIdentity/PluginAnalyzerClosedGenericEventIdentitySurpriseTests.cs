using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerClosedGenericEventIdentitySurpriseTests
{
    [Fact]
    public void Generated_event_kernel_preserves_or_rejects_closed_generic_event_identity()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(ClosedGenericEventKernelSource);
        var errorDiagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (errorDiagnostics.Any(diagnostic => diagnostic.Id == "DBXK100"))
        {
            Assert.Empty(result.GeneratedTrees);
            return;
        }

        Assert.Empty(errorDiagnostics);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain("IEventKernel<Probe.GenericEvent`1>", generated);
        Assert.DoesNotContain("HookSubscriptionManifest(\"Probe.GenericEvent`1\", \"GenericKernel\")", generated);

        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            ClosedGenericEventKernelSource,
            "Probe.GenericPluginPackage");

        Assert.DoesNotContain("Probe.GenericEvent`1", package.Manifest.Contract);
        Assert.DoesNotContain(
            package.Manifest.Subscriptions,
            subscription => string.Equals(subscription.Event, "Probe.GenericEvent`1", StringComparison.Ordinal));
        Assert.Contains("GenericEvent", package.Manifest.Contract, StringComparison.Ordinal);
        Assert.Contains("Int32", package.Manifest.Contract, StringComparison.Ordinal);
    }

    private const string ClosedGenericEventKernelSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Probe;

        public sealed record GenericEvent<T>(string TargetId, string Message, T Value);

        [Plugin("generic-kernel")]
        public sealed partial class GenericKernel : IEventKernel<GenericEvent<int>>
        {
            public bool ShouldHandle(GenericEvent<int> e, HookContext ctx)
                => e.Value > 0;

            public void Handle(GenericEvent<int> e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;
}
