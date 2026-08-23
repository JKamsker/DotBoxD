namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetEnumerableConstructorReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_hash_set_enumerable_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new HashSet<byte>(Enumerable.Repeat((byte)0, int.MaxValue))"),
            "DotBoxDPluginAnalyzerHashSetEnumerableConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.Contains("System.Collections.Generic.HashSet<byte>", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_empty_hash_set_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new HashSet<byte>()"),
            "DotBoxDPluginAnalyzerEmptyHashSetConstructorReachabilityTest");

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

                [Plugin("hash-set-enumerable-constructor-reachability")]
                public sealed class HashSetEnumerableConstructorKernel : IEventKernel<string>
                {
                    private static readonly HashSet<byte> Retained = {{initializer}};

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
