namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableOfTypeReachabilityTests
{
    [Fact]
    public async Task Reports_retained_linked_list_type_filter_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Enumerable.OfType<string>(Retained).GetEnumerator().MoveNext();"),
            "DotBoxDPluginAnalyzerEnumerableOfTypeReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.OfType", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string returnStatement)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-oftype-reachability")]
                public sealed class EnumerableOfTypeKernel : IEventKernel<string>
                {
                    private readonly LinkedList<object> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.AddLast(e.Length);
                        {{returnStatement}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
