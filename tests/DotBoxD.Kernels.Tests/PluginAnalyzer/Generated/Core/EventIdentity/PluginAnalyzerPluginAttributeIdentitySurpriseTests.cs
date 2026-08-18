using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerPluginAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_event_kernel_uses_the_real_plugin_attribute_id()
    {
        var foreignPluginAttribute = PluginServerGenerationTestDriver.CompileReference(
            """
            namespace DotBoxD.Abstractions;

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PluginAttribute : System.Attribute
            {
                public PluginAttribute(string id)
                {
                }
            }
            """,
            "ForeignPluginAttributes")
            .WithProperties(
                MetadataReferenceProperties.Assembly.WithAliases(["ForeignPluginAttributes"]));

        var result = PluginAnalyzerGeneratedPackageFactory.RunGeneratorWithReferences(
            Source,
            foreignPluginAttribute);

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain("foreign-id", generated, StringComparison.Ordinal);
        Assert.Contains("real-id", generated, StringComparison.Ordinal);
    }

    private const string Source = """
        extern alias ForeignPluginAttributes;

        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;

        namespace Regression;

        public sealed record SampleEvent(string TargetId, string Message);

        [ForeignPluginAttributes::DotBoxD.Abstractions.Plugin("foreign-id")]
        [Plugin("real-id")]
        public sealed partial class SampleKernel : IEventKernel<SampleEvent>
        {
            public bool ShouldHandle(SampleEvent e, HookContext ctx) => true;

            public void Handle(SampleEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;
}
