namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedSetComparerConstructorReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_sorted_set_enumerable_and_comparer_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new SortedSet<byte>(Enumerable.Repeat((byte)0, int.MaxValue), Comparer<byte>.Default)"),
            "DotBoxDPluginAnalyzerSortedSetComparerConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.SortedSet",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_sorted_set_comparer_only_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new SortedSet<byte>(Comparer<byte>.Default)"),
            "DotBoxDPluginAnalyzerSortedSetComparerOnlyConstructorReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string initializer)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-set-comparer-constructor-reachability")]
                public sealed class SortedSetComparerConstructorKernel : IEventKernel<string>
                {
                    private static readonly SortedSet<byte> Retained = {{initializer}};

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
