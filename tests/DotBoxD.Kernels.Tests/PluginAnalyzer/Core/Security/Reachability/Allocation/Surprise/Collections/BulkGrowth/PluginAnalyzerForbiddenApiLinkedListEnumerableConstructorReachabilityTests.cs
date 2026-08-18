namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiLinkedListEnumerableConstructorReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_linked_list_enumerable_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new LinkedList<byte>(Enumerable.Repeat((byte)0, int.MaxValue))"),
            "DotBoxDPluginAnalyzerLinkedListEnumerableConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.Contains("System.Collections.Generic.LinkedList", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_empty_linked_list_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new LinkedList<byte>()"),
            "DotBoxDPluginAnalyzerEmptyLinkedListConstructorReachabilityTest");

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

                [Plugin("linked-list-enumerable-constructor-reachability")]
                public sealed class LinkedListEnumerableConstructorKernel : IEventKernel<string>
                {
                    private static readonly LinkedList<byte> Retained = {{initializer}};

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
