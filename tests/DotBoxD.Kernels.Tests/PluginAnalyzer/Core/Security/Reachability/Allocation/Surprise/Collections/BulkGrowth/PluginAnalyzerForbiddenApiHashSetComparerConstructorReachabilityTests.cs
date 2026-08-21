namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetComparerConstructorReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_hash_set_enumerable_comparer_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new HashSet<byte>(Enumerable.Repeat((byte)0, int.MaxValue), EqualityComparer<byte>.Default)"),
            "DotBoxDPluginAnalyzerHashSetComparerConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.HashSet<byte>",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("new HashSet<byte>(EqualityComparer<byte>.Default)")]
    [InlineData("new HashSet<byte>()")]
    public async Task Does_not_report_hash_set_constructors_that_do_not_consume_a_collection(string initializer)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(initializer),
            "DotBoxDPluginAnalyzerHashSetSafeConstructorReachabilityTest");

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

                [Plugin("hash-set-comparer-constructor-reachability")]
                public sealed class HashSetComparerConstructorKernel : IEventKernel<string>
                {
                    private static readonly HashSet<byte> Retained = {{initializer}};

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
