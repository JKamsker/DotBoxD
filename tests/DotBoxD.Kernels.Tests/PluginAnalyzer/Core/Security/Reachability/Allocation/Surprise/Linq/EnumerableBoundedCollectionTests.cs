namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class EnumerableBoundedCollectionTests
{
    [Theory]
    [InlineData("Queue<int>")]
    [InlineData("Stack<int>")]
    [InlineData("LinkedList<int>")]
    [InlineData("SortedDictionary<int, int>")]
    public async Task Parameterless_any_preserves_framework_count_fast_paths(string collectionType)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;

            [Plugin("bounded-count")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly {{collectionType}} Values = new();
                public bool ShouldHandle(string e, HookContext context) => Values.Any() || Enumerable.Any(Values);
                public void Handle(string e, HookContext context) { }
            }
            """;
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "BoundedCount");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Theory]
    [InlineData("Enumerable.Any(Values)", false)]
    [InlineData("Values.Any()", false)]
    [InlineData("Enumerable.Any(Values, value => value == 1)", true)]
    [InlineData("Enumerable.Contains(Set, 1)", false)]
    [InlineData("Set.Contains(1)", false)]
    [InlineData("Enumerable.Contains(Set, 1, null)", true)]
    [InlineData("Enumerable.Contains(Values, 1)", true)]
    public async Task Linq_collection_calls_preserve_bounded_controls(string expression, bool reportsScan)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            [Plugin("bounded-enumerable")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly List<int> Values = new();
                private readonly HashSet<int> Set = new();
                public bool ShouldHandle(string e, HookContext context) => {{expression}};
                public void Handle(string e, HookContext context) { }
            }
            """;
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "BoundedEnumerable");
        Assert.Equal(reportsScan ? 1 : 0, diagnostics.Count(diagnostic => diagnostic.Id == "DBXK001"));
    }
}
