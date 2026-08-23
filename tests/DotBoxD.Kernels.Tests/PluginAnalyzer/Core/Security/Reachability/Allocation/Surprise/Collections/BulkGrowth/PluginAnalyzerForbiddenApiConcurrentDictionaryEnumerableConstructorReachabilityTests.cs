namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiConcurrentDictionaryEnumerableConstructorReachabilityTests
{
    [Theory]
    [InlineData("new ConcurrentDictionary<int, int>(Enumerable.Range(0, int.MaxValue).Select(static value => new KeyValuePair<int, int>(value, value)))")]
    [InlineData("new ConcurrentDictionary<int, int>(Enumerable.Range(0, int.MaxValue).Select(static value => new KeyValuePair<int, int>(value, value)), EqualityComparer<int>.Default)")]
    [InlineData("new ConcurrentDictionary<int, int>(1, Enumerable.Range(0, int.MaxValue).Select(static value => new KeyValuePair<int, int>(value, value)), EqualityComparer<int>.Default)")]
    public async Task Reports_unbounded_concurrent_dictionary_enumerable_constructor_static_initializer(
        string initializer)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(initializer),
            "DotBoxDPluginAnalyzerConcurrentDictionaryEnumerableConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Concurrent.ConcurrentDictionary", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_default_concurrent_dictionary_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new ConcurrentDictionary<int, int>()"),
            "DotBoxDPluginAnalyzerDefaultConcurrentDictionaryEnumerableConstructorReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string initializer)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections.Concurrent;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("concurrent-dictionary-enumerable-constructor-reachability")]
                public sealed class ConcurrentDictionaryEnumerableConstructorKernel : IEventKernel<string>
                {
                    private static readonly ConcurrentDictionary<int, int> Retained = {{initializer}};

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
