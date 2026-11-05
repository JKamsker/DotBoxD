namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableExceptReachabilityTests
{
    [Fact]
    public async Task Reports_materialization_of_retained_second_source()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerEnumerableExceptReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.Except", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-except-reachability")]
                public sealed class EnumerableExceptKernel : IEventKernel<string>
                {
                    private readonly List<int> retained = new();

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context)
                    {
                        retained.Add(0);
                        _ = retained.Count;
                        _ = Enumerable.Except(Enumerable.Repeat(0, 1), Enumerate()).GetEnumerator().MoveNext();
                    }

                    private IEnumerable<int> Enumerate()
                    {
                        foreach (var value in retained)
                        {
                            yield return value;
                        }
                    }
                }
            }
            """;
}
