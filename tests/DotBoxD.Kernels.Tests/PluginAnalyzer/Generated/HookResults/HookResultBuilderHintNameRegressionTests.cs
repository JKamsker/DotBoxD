using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultBuilderHintNameRegressionTests
{
    [Fact]
    public void HookResult_builders_use_collision_free_hint_names_for_distinct_namespaces()
    {
        var generated = PluginAnalyzerGeneratedPackageFactory.GeneratedSources("""
            using DotBoxD.Abstractions;

            namespace A.B
            {
                [HookResult]
                public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);
            }

            namespace A_B
            {
                [HookResult]
                public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);
            }
            """);

        Assert.Equal(
            2,
            generated.Count(source => source.Contains("partial record struct DamageResult", StringComparison.Ordinal)));
    }
}
