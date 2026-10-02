namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class FrameworkCollectionScanIdentityTests
{
    [Theory]
    [InlineData("System.Linq", "Enumerable", "public static bool Any(int[] values) => false;", "System.Linq.Enumerable.Any(null)")]
    [InlineData("System.Linq", "Enumerable", "public static bool Contains(int[] values, int value) => false;", "System.Linq.Enumerable.Contains(null, 1)")]
    [InlineData("System.Collections.Generic", "Dictionary<TKey, TValue>", "public bool ContainsValue(TValue value) => false;", "new System.Collections.Generic.Dictionary<int, int>().ContainsValue(1)")]
    [InlineData("System.Collections", "Hashtable", "public bool ContainsValue(object value) => false;", "new System.Collections.Hashtable().ContainsValue(1)")]
    [InlineData("System.Collections.Generic", "SortedSet<T>", "public bool IsSupersetOf(T[] values) => false;", "new System.Collections.Generic.SortedSet<int>().IsSupersetOf(null)")]
    [InlineData("System.Collections.Generic", "SortedDictionary<TKey, TValue>", "public bool ContainsValue(TValue value) => false;", "new System.Collections.Generic.SortedDictionary<int, int>().ContainsValue(1)")]
    [InlineData("System.Collections.Generic", "SortedList<TKey, TValue>", "public bool ContainsValue(TValue value) => false;", "new System.Collections.Generic.SortedList<int, int>().ContainsValue(1)")]
    public async Task Source_defined_framework_name_lookalikes_do_not_report_scans(
        string ns, string type, string member, string expression)
    {
        var source = $$"""
            namespace {{ns}}
            {
                public sealed class {{type}} { {{member}} }
            }
            namespace Sample
            {
                [DotBoxD.Abstractions.Plugin("collection-identity")]
                public sealed class Kernel : DotBoxD.Abstractions.IEventKernel<string>
                {
                    public bool ShouldHandle(string e, DotBoxD.Abstractions.HookContext context) => {{expression}};
                    public void Handle(string e, DotBoxD.Abstractions.HookContext context) { }
                }
            }
            """;
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(source, "CollectionIdentity");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }
}
