using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_event_kernel_ignores_aliased_lookalike_hook_attributes()
    {
        var foreignHookAttribute = PluginServerGenerationTestDriver.CompileReference(
            """
            namespace DotBoxD.Abstractions;

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class HookAttribute : System.Attribute
            {
                public HookAttribute(string name)
                {
                }
            }
            """,
            "ForeignHookAttributes")
            .WithProperties(
                MetadataReferenceProperties.Assembly.WithAliases(["ForeignHookAttributes"]));

        var result = PluginAnalyzerGeneratedPackageFactory.RunGeneratorWithReferences(
            Source,
            foreignHookAttribute);

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain("foreign.declared.hook", generated, StringComparison.Ordinal);
        Assert.Contains("Regression.ForeignMarkedEvent", generated, StringComparison.Ordinal);
        Assert.Contains("real.declared.hook", generated, StringComparison.Ordinal);
    }

    private const string Source = """
        extern alias ForeignHookAttributes;

        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;

        namespace Regression;

        [ForeignHookAttributes::DotBoxD.Abstractions.Hook("foreign.declared.hook")]
        public sealed record ForeignMarkedEvent(string TargetId, string Message);

        [Hook("real.declared.hook")]
        public sealed record RealMarkedEvent(string TargetId, string Message);

        [Plugin("foreign-hook-identity")]
        public sealed partial class ForeignMarkedKernel : IEventKernel<ForeignMarkedEvent>
        {
            public bool ShouldHandle(ForeignMarkedEvent e, HookContext ctx) => true;

            public void Handle(ForeignMarkedEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }

        [Plugin("real-hook-identity")]
        public sealed partial class RealMarkedKernel : IEventKernel<RealMarkedEvent>
        {
            public bool ShouldHandle(RealMarkedEvent e, HookContext ctx) => true;

            public void Handle(RealMarkedEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;
}
