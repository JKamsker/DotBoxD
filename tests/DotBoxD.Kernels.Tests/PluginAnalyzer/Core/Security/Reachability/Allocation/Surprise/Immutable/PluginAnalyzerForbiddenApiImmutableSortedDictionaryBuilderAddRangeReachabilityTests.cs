namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiImmutableSortedDictionaryBuilderAddRangeReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_immutable_sorted_dictionary_builder_add_range_in_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerImmutableSortedDictionaryBuilderAddRangeReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>.Builder.AddRange",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Collections.Immutable;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("immutable-sorted-dictionary-builder-add-range")]
                public sealed class ImmutableSortedDictionaryBuilderAddRangeKernel : IEventKernel<string>
                {
                    private static readonly ImmutableSortedDictionary<int, int>.Builder Retained = CreateRetained();

                    public bool ShouldHandle(string e, HookContext context) => Retained.Count >= 0;

                    public void Handle(string e, HookContext context) { }

                    private static ImmutableSortedDictionary<int, int>.Builder CreateRetained()
                    {
                        var builder = ImmutableSortedDictionary.CreateBuilder<int, int>();
                        builder.AddRange(Enumerable.Range(0, int.MaxValue).Select(index => KeyValuePair.Create(index, index)));
                        return builder;
                    }
                }
            }
            """;
}
