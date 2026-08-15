using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerLiveSettingAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_package_ignores_aliased_lookalike_live_setting_attribute()
    {
        var foreignLiveSetting = PluginServerGenerationTestDriver.CompileReference(
                """
                namespace DotBoxD.Abstractions;

                public sealed class LiveSettingAttribute : System.Attribute;
                """,
                "ForeignLiveSetting")
            .WithAliases(ImmutableArray.Create("ForeignLiveSetting"));
        var result = PluginAnalyzerGeneratedPackageFactory.RunGeneratorWithReferences(
            """
            extern alias ForeignLiveSetting;

            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            [Plugin("foreign-live-setting")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [ForeignLiveSetting::DotBoxD.Abstractions.LiveSetting]
                public int ForeignSetting { get; set; } = 7;

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """,
            foreignLiveSetting);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(
            "LiveSettingDefinition(\"ForeignSetting\"",
            generated,
            StringComparison.Ordinal);
    }
}
