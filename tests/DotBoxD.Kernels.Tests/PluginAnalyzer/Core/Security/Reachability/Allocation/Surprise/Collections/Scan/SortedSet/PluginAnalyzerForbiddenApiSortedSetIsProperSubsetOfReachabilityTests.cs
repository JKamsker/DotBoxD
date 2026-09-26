namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedSetIsProperSubsetOfReachabilityTests
{
    [Fact]
    public async Task Reports_sorted_set_is_proper_subset_of_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.IsProperSubsetOf(Enumerable.Range(-1, int.MaxValue));"),
            "DotBoxDPluginAnalyzerSortedSetIsProperSubsetOfReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.SortedSet.IsProperSubsetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_sorted_set_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Add(-1);
                return Retained.Count > 0;
                """),
            "DotBoxDPluginAnalyzerSortedSetIsProperSubsetOfControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Does_not_report_source_defined_sorted_set_lookalike()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            """
            #nullable enable

            namespace System.Collections.Generic
            {
                public sealed class SortedSet<T>
                {
                    public bool IsProperSubsetOf(IEnumerable<T> other) => false;
                }
            }

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("source-defined-sorted-set-lookalike")]
                public sealed class SourceDefinedSortedSetKernel : IEventKernel<string>
                {
                    private static readonly SortedSet<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return Retained.IsProperSubsetOf(Enumerable.Range(-1, int.MaxValue));
                    }

                    public void Handle(string e, HookContext context)
                    {
                    }
                }
            }
            """,
            "DotBoxDPluginAnalyzerSourceDefinedSortedSetLookalikeTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-set-is-proper-subset-of-reachability")]
                public sealed class SortedSetIsProperSubsetOfKernel : IEventKernel<string>
                {
                    private static readonly SortedSet<int> Retained = [-1];

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(string e, HookContext context)
                    {
                    }
                }
            }
            """;
}
