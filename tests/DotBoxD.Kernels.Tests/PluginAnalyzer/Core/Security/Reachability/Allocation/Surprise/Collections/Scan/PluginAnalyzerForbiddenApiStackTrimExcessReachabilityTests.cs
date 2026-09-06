namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiStackTrimExcessReachabilityTests
{
    [Fact]
    public async Task Reports_stack_trim_excess_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Push(e);
                Retained.TrimExcess();
                """),
            "DotBoxDPluginAnalyzerStackTrimExcessReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.Stack.TrimExcess",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_stack_push_and_count()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                if (Retained.Count < 4)
                {
                    Retained.Push(e);
                }
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerStackPushAndCountReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("stack-trim-excess-reachability")]
                public sealed class StackTrimExcessKernel : IEventKernel<string>
                {
                    private static readonly Stack<string> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
