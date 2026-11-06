using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

/// <summary>
/// A captured local is only folded to its initializer when it is effectively final. A write through a
/// <c>ref</c> alias (or a pointer) mutates it just like a direct assignment, so it must not be frozen.
/// </summary>
public sealed class HookChainCapturedRefLocalTests
{
    [Theory]
    [InlineData("ref var alias = ref limit; alias = 50;")]
    [InlineData("ref int alias = ref limit; alias++;")]
    public void Ref_alias_write_is_treated_like_a_direct_write(string mutation)
    {
        var direct = RunGenerator(Source("limit = 50;"));
        var throughRef = RunGenerator(Source(mutation));

        Assert.Equal(Ids(direct), Ids(throughRef));
        Assert.Equal(
            GeneratedText(direct).Count(text => text.Contains("HookChain_", StringComparison.Ordinal)),
            GeneratedText(throughRef).Count(text => text.Contains("HookChain_", StringComparison.Ordinal)));
    }

    [Fact]
    public void Unmutated_captured_local_is_still_folded()
    {
        var result = RunGenerator(Source(string.Empty));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(GeneratedText(result), text => text.Contains("HookChain_", StringComparison.Ordinal));
    }

    private static string[] Ids(GeneratorDriverRunResult result)
        => result.Diagnostics.Select(diagnostic => diagnostic.Id).Order(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> GeneratedText(GeneratorDriverRunResult result)
        => result.GeneratedTrees.Select(tree => tree.ToString());

    private static string Source(string mutation)
        => $$"""
            using DotBoxD.Plugins.Runtime;

            namespace ChainSample;

            public sealed record CapturedEvent(string TargetId, int Amount);

            public static class Usage
            {
                public static void Configure(SubscriptionRegistry subscriptions)
                {
                    var limit = 10;
                    {{mutation}}
                    subscriptions.On<CapturedEvent>()
                        .Where(e => e.Amount > limit)
                        .Select(e => e.TargetId)
                        .Run((id, ctx) => ctx.Messages.Send(id, "hit"));
                }
            }
            """;
}
