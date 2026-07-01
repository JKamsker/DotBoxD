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

    [Fact]
    public void HookResult_builders_support_keyword_escaped_namespace_segments()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources("""
            using DotBoxD.Abstractions;

            namespace Sample.@event;

            [HookResult]
            public readonly partial record struct KeywordNamespaceResult(
                bool Success,
                string? Reason,
                int Damage);
            """));

        Assert.Contains("namespace Sample.@event", generated, StringComparison.Ordinal);
        Assert.Contains(
            "partial record struct KeywordNamespaceResult : global::DotBoxD.Abstractions.IHookResult",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("public KeywordNamespaceResult WithDamage(int damage)", generated, StringComparison.Ordinal);
    }
}
