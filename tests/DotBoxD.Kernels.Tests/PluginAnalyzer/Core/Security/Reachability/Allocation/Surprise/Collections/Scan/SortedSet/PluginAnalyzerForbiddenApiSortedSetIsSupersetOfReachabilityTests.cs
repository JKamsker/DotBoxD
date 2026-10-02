namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedSetIsSupersetOfReachabilityTests
{
    [Fact]
    public async Task Reports_retained_sorted_set_is_superset_of_scan_in_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(e);\n                    return Retained.IsSupersetOf(Retained.Select(static value => value));"),
            "DotBoxDPluginAnalyzerSortedSetIsSupersetOfReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.SortedSet.IsSupersetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_sorted_set_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(e);\n                    return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerSortedSetIsSupersetOfAddControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Does_not_report_source_defined_sorted_set_lookalike()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "Retained.Add(e);\n                    return Retained.IsSupersetOf(Enumerable.Repeat(e, 1));",
                "SortedSet<byte>",
                "internal sealed class SortedSet<T> { public SortedSet() { } public void Add(T value) { } public bool IsSupersetOf(IEnumerable<T> values) => true; }"),
            "DotBoxDPluginAnalyzerSourceDefinedSortedSetIsSupersetOfControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(
        string shouldHandleBody,
        string retainedType = "System.Collections.Generic.SortedSet<int>",
        string supportingType = "")
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                {{supportingType}}

                [Plugin("sorted-set-is-superset-of-reachability")]
                public sealed class SortedSetIsSupersetOfKernel : IEventKernel<byte>
                {
                    private static readonly {{retainedType}} Retained = new();

                    public bool ShouldHandle(byte e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(byte e, HookContext context) { }
                }
            }
            """;
}
