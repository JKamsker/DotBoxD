namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiStackEnsureCapacityReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_stack_ensure_capacity_from_static_initializer_helper()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerStackEnsureCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.Stack", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("stack-ensure-capacity-reachability")]
                public sealed class StackEnsureCapacityKernel : IEventKernel<string>
                {
                    private static readonly Stack<byte> Retained = CreateRetained();

                    public bool ShouldHandle(string e, HookContext context) => Retained.Count >= 0;

                    public void Handle(string e, HookContext context) { }

                    private static Stack<byte> CreateRetained()
                    {
                        var retained = new Stack<byte>();
                        retained.EnsureCapacity(int.MaxValue);
                        return retained;
                    }
                }
            }
            """;
}
