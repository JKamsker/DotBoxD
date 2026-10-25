namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class EnumerableRuntimeIdentitySurpriseTests
{
    [Theory]
    [InlineData("Last")]
    [InlineData("LastOrDefault")]
    [InlineData("Count")]
    [InlineData("Any")]
    [InlineData("Single")]
    [InlineData("First")]
    public async Task Static_list_type_does_not_prove_a_framework_indexer_at_runtime(string name)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync($$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;
            [Plugin("runtime-list")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly List<int> Values = new ScanningList();
                public bool ShouldHandle(string e, HookContext context)
                {
                    Values.Add(e.Length);
                    return Enumerable.{{name}}(Values){{(name == "Any" ? "" : " > 0")}};
                }
                public void Handle(string e, HookContext context) { }
            }
            public sealed class ScanningList : List<int>, IList<int>
            {
                int ICollection<int>.Count => Enumerable.Sum(this);
                int IList<int>.this[int index]
                {
                    get => Enumerable.Sum(this);
                    set => base[index] = value;
                }
            }
            """, "RuntimeList" + name);
        Assert.Contains(diagnostics, d => d.Id == "DBXK001" &&
            d.GetMessage().Contains("System.Linq.Enumerable." + name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("(values, _) = (new ScanningList(), 0);")]
    [InlineData("Replace(out values);")]
    public async Task Local_collection_replacement_does_not_retain_an_exact_list_proof(string replacement)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync($$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;
            [Plugin("local-runtime-list")]
            public sealed class Kernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    List<int> values = new();
                    {{replacement}}
                    return Enumerable.Last(values) > 0;
                }
                private static void Replace(out List<int> values) => values = new ScanningList();
                public void Handle(string e, HookContext context) { }
            }
            public sealed class ScanningList : List<int>, IList<int>
            {
                int ICollection<int>.Count => Enumerable.Sum(this);
            }
            """, "ReplacedLocalList");
        Assert.Contains(diagnostics, d => d.Id == "DBXK001" && d.GetMessage().Contains("System.Linq.Enumerable.Last", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Constructor_reassignment_is_not_an_exact_initializer_but_unrelated_subclasses_are_harmless(bool replace)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync($$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;
            [Plugin("constructor-runtime-list")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly List<int> Values = new();
                public Kernel() { {{(replace ? "Values = new ScanningList();" : "Values.Add(1);")}} }
                public bool ShouldHandle(string e, HookContext context) => Enumerable.Last(Values) > 0;
                public void Handle(string e, HookContext context) { }
            }
            public sealed class ScanningList : List<int>, IList<int>
            {
                int ICollection<int>.Count => Enumerable.Sum(this);
            }
            """, "ConstructorList");
        Assert.Equal(replace, diagnostics.Any(d => d.Id == "DBXK001" && d.GetMessage().Contains("System.Linq.Enumerable.Last", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("OrderBy")]
    [InlineData("Reverse")]
    [InlineData("ToList")]
    public async Task Source_defined_enumerable_lookalikes_are_not_framework_materializers(string name)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync($$"""
            using DotBoxD.Abstractions;
            [Plugin("lookalike-materializer")]
            public sealed class Kernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context) => System.Linq.Enumerable.{{name}}(e.Length);
                public void Handle(string e, HookContext context) { }
            }
            namespace System.Linq
            {
                public static class Enumerable { public static bool {{name}}(int value) => value > 0; }
            }
            """, "LookalikeEnumerable" + name);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }
    [Theory]
    [InlineData("Last")]
    [InlineData("LastOrDefault")]
    [InlineData("Count")]
    [InlineData("Any")]
    [InlineData("Single")]
    public async Task Backward_goto_does_not_keep_a_stale_exact_local_origin(string name)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync($$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;
            [Plugin("goto-runtime-list")]
            public sealed class Kernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    List<int> values = new() { 1 };
                    int pass = 0;
                again:
                    bool result = Enumerable.{{name}}(values){{(name == "Any" ? "" : " > 0")}};
                    if (pass++ == 0) { values = new ScanningList(); goto again; }
                    return result;
                }
                public void Handle(string e, HookContext context) { }
            }
            public sealed class ScanningList : List<int>, IList<int>
            {
                int ICollection<int>.Count => Enumerable.Sum(this);
            }
            """, "GotoRuntimeList" + name);
        Assert.Contains(diagnostics, d => d.Id == "DBXK001" && d.GetMessage().Contains("System.Linq.Enumerable." + name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Single", "HashSet<int>", "new()", "Values", true)]
    [InlineData("First", "HashSet<int>", "new()", "Values", true)]
    [InlineData("Single", "Dictionary<int, int>", "new()", "Values.Keys", true)]
    [InlineData("First", "Dictionary<int, int>", "new()", "Values.Keys", true)]
    [InlineData("Single", "List<int>", "new()", "Values", false)]
    [InlineData("First", "List<int>", "new()", "Values", false)]
    [InlineData("Single", "int[]", "[]", "Values", false)]
    [InlineData("First", "int[]", "[]", "Values", false)]
    public async Task First_and_Single_require_an_indexed_fast_path_rather_than_collection_count(string name, string type, string initializer, string expression, bool forbidden)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync($$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;
            [Plugin("single-fast-path")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly {{type}} Values = {{initializer}};
                public bool ShouldHandle(string e, HookContext context) => Enumerable.{{name}}({{expression}}) > 0;
                public void Handle(string e, HookContext context) { }
            }
            """, "SingleFastPath");
        Assert.Equal(forbidden, diagnostics.Any(d => d.Id == "DBXK001"));
    }

    [Theory]
    [InlineData("Last", false)]
    [InlineData("Last", true)]
    [InlineData("First", false)]
    [InlineData("First", true)]
    public async Task Foreign_two_argument_scan_name_is_not_a_framework_scan(string name, bool methodGroup)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync($$"""
            using System;
            using DotBoxD.Abstractions;
            [Plugin("foreign-last")]
            public sealed class Kernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    {{(methodGroup ? $"Func<int, int, bool> scan = System.Linq.Enumerable.{name}; return scan(e.Length, 0);" : $"return System.Linq.Enumerable.{name}(e.Length, 0);")}}
                }
                public void Handle(string e, HookContext context) { }
            }
            namespace System.Linq
            {
                public static class Enumerable { public static bool {{name}}(int value, int minimum) => value > minimum; }
            }
            """, "ForeignLast");
        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conversion_operators_and_init_accessors_do_not_prove_exact_list_origins(bool initAccessor)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync($$"""
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Abstractions;
            [Plugin("indirect-runtime-list")]
            public sealed class Kernel : IEventKernel<string>
            {
                private readonly List<int> Values = new();
                public List<int> Replacement { init => Values = value; }
                public Kernel() { {{(initAccessor ? "Replacement = new ScanningList();" : "")}} }
                public bool ShouldHandle(string e, HookContext context)
                {
                    {{(initAccessor ? "List<int> values = Values;" : "List<int> values = (List<int>)(Wrapper)new List<int>();")}}
                    return Enumerable.Last(values) > 0;
                }
                public void Handle(string e, HookContext context) { }
            }
            public sealed class Wrapper
            {
                public static explicit operator Wrapper(List<int> values) => new();
                public static explicit operator List<int>(Wrapper value) => new ScanningList();
            }
            public sealed class ScanningList : List<int>, IList<int>
            {
                int IList<int>.this[int index] { get => Enumerable.Sum(this); set => base[index] = value; }
            }
            """, "IndirectRuntimeList");
        Assert.Contains(diagnostics, d => d.Id == "DBXK001");
    }

}
