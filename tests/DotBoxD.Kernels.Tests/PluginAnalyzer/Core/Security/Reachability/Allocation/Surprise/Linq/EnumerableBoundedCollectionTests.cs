namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class EnumerableBoundedCollectionTests
{
    [Theory]
    [InlineData("HashSet<string>", "Values", "string", false)]
    [InlineData("HashSet<string>", "Values", "object", true)]
    [InlineData("List<string>", "Values", "string", false)]
    [InlineData("List<string>", "Values", "object", false)]
    [InlineData("Dictionary<string, string>", "Values.Keys", "object", false)]
    [InlineData("Dictionary<string, string>", "Values.Values", "object", false)]
    [InlineData("SortedList<string, string>", "Values.Keys", "object", false)]
    [InlineData("SortedList<string, string>", "Values.Values", "object", false)]
    public async Task Any_fast_path_requires_compatible_generic_or_nongeneric_collection(
        string collectionType, string sourceExpression, string elementType, bool reportsScan)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;

            [Plugin("bounded-any-element-type")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly {{collectionType}} Values = new();
                public bool ShouldHandle(string e, HookContext context)
                    => Enumerable.Any<{{elementType}}>({{sourceExpression}});
                public void Handle(string e, HookContext context) { }
            }
            """;
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "BoundedAnyElementType");
        Assert.Equal(reportsScan ? 1 : 0, diagnostics.Count(diagnostic => diagnostic.Id == "DBXK001"));
    }

    [Theory]
    [InlineData("HashSet<string>", "Values", "string", false)]
    [InlineData("HashSet<string>", "Values", "object", true)]
    [InlineData("SortedSet<string>", "Values", "string", false)]
    [InlineData("SortedSet<string>", "Values", "object", true)]
    [InlineData("Dictionary<string, int>", "Values.Keys", "string", false)]
    [InlineData("Dictionary<string, int>", "Values.Keys", "object", true)]
    [InlineData("SortedDictionary<string, int>", "Values.Keys", "string", false)]
    [InlineData("SortedDictionary<string, int>", "Values.Keys", "object", true)]
    [InlineData("SortedList<string, int>", "Values.Keys", "string", false)]
    [InlineData("SortedList<string, int>", "Values.Keys", "object", true)]
    public async Task Contains_fast_path_requires_matching_collection_element_type(
        string collectionType, string sourceExpression, string elementType, bool reportsScan)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;

            [Plugin("bounded-contains-element-type")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly {{collectionType}} Values = new();
                public bool ShouldHandle(string e, HookContext context)
                    => Enumerable.Contains<{{elementType}}>({{sourceExpression}}, "missing");
                public void Handle(string e, HookContext context) { }
            }
            """;
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "BoundedContainsElementType");
        Assert.Equal(reportsScan ? 1 : 0, diagnostics.Count(diagnostic => diagnostic.Id == "DBXK001"));
    }

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

    [Theory]
    [InlineData("List<int>", "new()", "Enumerable.LastOrDefault(Values)", false)]
    [InlineData("List<int>", "new()", "Enumerable.LastOrDefault(Values, -1)", false)]
    [InlineData("int[]", "[]", "Enumerable.LastOrDefault(Values)", false)]
    [InlineData("List<int>", "new()", "Enumerable.LastOrDefault(Values, static _ => true)", true)]
    [InlineData("List<int>", "new()", "Enumerable.LastOrDefault(Values, static _ => true, -1)", true)]
    public async Task Last_or_default_preserves_indexed_fast_paths(
        string collectionType,
        string initializer,
        string expression,
        bool reportsScan)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;

            [Plugin("bounded-last-or-default")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly {{collectionType}} Values = {{initializer}};
                public bool ShouldHandle(string e, HookContext context) => {{expression}} >= 0;
                public void Handle(string e, HookContext context) { }
            }
            """;
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "BoundedLastOrDefault");
        Assert.Equal(reportsScan ? 1 : 0, diagnostics.Count(diagnostic => diagnostic.Id == "DBXK001"));
    }

    [Theory]
    [InlineData("Dictionary<int, int>", "Map.Keys.Any()", false)]
    [InlineData("Dictionary<int, int>", "Enumerable.Any(Map.Keys)", false)]
    [InlineData("Dictionary<int, int>", "Map.Values.Any()", false)]
    [InlineData("Dictionary<int, int>", "Enumerable.Any(Map.Values)", false)]
    [InlineData("Dictionary<int, int>", "Enumerable.Contains(Map.Keys, 1)", false)]
    [InlineData("Dictionary<int, int>", "Enumerable.Contains(Map.Values, 1)", true)]
    [InlineData("Dictionary<int, int>", "Enumerable.Contains(Map.Keys, 1, null)", true)]
    [InlineData("Dictionary<int, int>", "Map.Keys.Any(value => value == 1)", true)]
    [InlineData("Dictionary<int, int>", "Map.Values.Any(value => value == 1)", true)]
    [InlineData("SortedDictionary<int, int>", "Map.Keys.Any()", false)]
    [InlineData("SortedDictionary<int, int>", "Enumerable.Any(Map.Keys)", false)]
    [InlineData("SortedDictionary<int, int>", "Map.Values.Any()", false)]
    [InlineData("SortedDictionary<int, int>", "Enumerable.Any(Map.Values)", false)]
    [InlineData("SortedDictionary<int, int>", "Enumerable.Contains(Map.Keys, 1)", false)]
    [InlineData("SortedDictionary<int, int>", "Enumerable.Contains(Map.Values, 1)", true)]
    [InlineData("SortedDictionary<int, int>", "Enumerable.Contains(Map.Keys, 1, null)", true)]
    [InlineData("SortedDictionary<int, int>", "Map.Keys.Any(value => value == 1)", true)]
    [InlineData("SortedDictionary<int, int>", "Map.Values.Any(value => value == 1)", true)]
    [InlineData("SortedList<int, int>", "Map.Keys.Any()", false)]
    [InlineData("SortedList<int, int>", "Enumerable.Any(Map.Keys)", false)]
    [InlineData("SortedList<int, int>", "Map.Values.Any()", false)]
    [InlineData("SortedList<int, int>", "Enumerable.Any(Map.Values)", false)]
    [InlineData("SortedList<int, int>", "Enumerable.Contains(Map.Keys, 1)", false)]
    [InlineData("SortedList<int, int>", "Enumerable.Contains(Map.Keys, 1, null)", true)]
    [InlineData("SortedList<int, int>", "Enumerable.Contains(Map.Values, 1)", true)]
    [InlineData("SortedList<int, int>", "Enumerable.Contains(Map.Values, 1, null)", true)]
    [InlineData("SortedList<int, int>", "Map.Keys.Any(value => value == 1)", true)]
    [InlineData("SortedList<int, int>", "Map.Values.Any(value => value == 1)", true)]
    public async Task Framework_dictionary_views_preserve_bounded_calls_and_scan_diagnostics(
        string collectionType, string expression, bool reportsScan)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;

            [Plugin("bounded-dictionary-view")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly {{collectionType}} Map = new();
                public bool ShouldHandle(string e, HookContext context) => {{expression}};
                public void Handle(string e, HookContext context) { }
            }
            """;
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "BoundedDictionaryView");
        Assert.Equal(reportsScan ? 1 : 0, diagnostics.Count(diagnostic => diagnostic.Id == "DBXK001"));
    }
}
