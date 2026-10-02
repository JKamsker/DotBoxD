namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedListValuesScanReachabilityTests
{
    [Theory]
    [InlineData("Retained.Values.Contains(\"missing\")", "System.Collections.Generic.ICollection.Contains")]
    [InlineData("Retained.Values.IndexOf(\"missing\") >= 0", "System.Collections.Generic.IList.IndexOf")]
    public async Task Reports_retained_sorted_list_values_scan_in_reachable_event_kernel(
        string valueLookup,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(valueLookup),
            "DotBoxDPluginAnalyzerSortedListValuesScanReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(expectedApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("values.Contains(\"missing\")", "System.Collections.Generic.ICollection.Contains")]
    [InlineData("alias.IndexOf(\"missing\") >= 0", "System.Collections.Generic.IList.IndexOf")]
    public async Task Reports_values_scan_through_local_alias(string expression, string expectedApi)
    {
        var source = Source(expression).Replace(
            "return " + expression,
            "var values = Retained.Values; var alias = values; return " + expression,
            StringComparison.Ordinal);
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "SortedListValuesAlias");
        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(expectedApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ICollection<string> values = Retained.Values; values = new HashSet<string>();", "values", false)]
    [InlineData("ICollection<string> values = new HashSet<string>(); values = Retained.Values;", "values", true)]
    [InlineData("ICollection<string> values; values = Retained.Values;", "values", true)]
    [InlineData("ICollection<string> values = Retained.Values; if (e.Length == 0) values = new HashSet<string>();", "values", true)]
    [InlineData("ICollection<string> values = new HashSet<string>(); if (e.Length == 0) values = Retained.Values;", "values", true)]
    [InlineData("ICollection<string> values = Retained.Values; var alias = values; values = new HashSet<string>();", "alias", true)]
    [InlineData("ICollection<string> values = new HashSet<string>(); var alias = values; values = Retained.Values;", "alias", false)]
    [InlineData("ICollection<string> values = new HashSet<string>(); values = Retained.Values; var alias = values; var chained = alias;", "chained", true)]
    [InlineData("ICollection<string> values = Retained.Values; { values = new HashSet<string>(); }", "values", false)]
    [InlineData("ICollection<string> values = Retained.Values; values = (ICollection<string>)values;", "values", true)]
    [InlineData("ICollection<string> values = new HashSet<string>(); values = (ICollection<string>)values;", "values", false)]
    public async Task Values_origins_follow_assignments_at_the_time_of_each_read(
        string statements, string receiver, bool reportsScan)
    {
        var expression = receiver + ".Contains(e)";
        var source = Source(expression).Replace(
            "return " + expression,
            statements + " return " + expression,
            StringComparison.Ordinal);
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "SortedListValuesAssignments");
        Assert.Equal(reportsScan ? 1 : 0, diagnostics.Count(diagnostic => diagnostic.Id == "DBXK001"));
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_sorted_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Count > 0"),
            "DotBoxDPluginAnalyzerSortedListValuesScanAddCountControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string returnExpression)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-list-values-scan-reachability")]
                public sealed class SortedListValuesScanKernel : IEventKernel<string>
                {
                    private static readonly SortedList<int, string> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(Retained.Count, e);
                        return {{returnExpression}};
                    }

                    public void Handle(string e, HookContext context)
                    {
                    }
                }
            }
            """;
}
