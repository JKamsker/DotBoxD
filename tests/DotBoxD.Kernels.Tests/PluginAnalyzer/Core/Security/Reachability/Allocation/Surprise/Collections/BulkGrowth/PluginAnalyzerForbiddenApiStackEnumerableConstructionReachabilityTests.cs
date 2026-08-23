namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiStackEnumerableConstructionReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_stack_enumerable_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new Stack<byte>(Enumerable.Repeat((byte)0, int.MaxValue))"),
            "DotBoxDPluginAnalyzerStackEnumerableConstructionReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.Contains("System.Collections.Generic.Stack", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_empty_stack_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new Stack<byte>()"),
            "DotBoxDPluginAnalyzerEmptyStackConstructorReachabilityTest");

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

                [Plugin("stack-enumerable-construction-reachability")]
                public sealed class StackEnumerableConstructionKernel : IEventKernel<string>
                {
                    private static readonly Stack<byte> Retained = {{initializer}};

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
