namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedDictionaryDictionaryConstructorReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_sorted_dictionary_dictionary_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new SortedDictionary<int, int>(Source)"),
            "DotBoxDPluginAnalyzerSortedDictionaryDictionaryConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.SortedDictionary", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_empty_sorted_dictionary_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new SortedDictionary<int, int>()"),
            "DotBoxDPluginAnalyzerEmptySortedDictionaryConstructorReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string initializer)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-dictionary-dictionary-constructor-reachability")]
                public sealed class SortedDictionaryDictionaryConstructorKernel : IEventKernel<string>
                {
                    private static readonly IDictionary<int, int> Source = new LazilySizedDictionary(int.MaxValue);
                    private static readonly SortedDictionary<int, int> Retained = {{initializer}};

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }

                internal sealed class LazilySizedDictionary(int entryCount) : IDictionary<int, int>
                {
                    public int Count => entryCount;

                    public bool IsReadOnly => true;

                    public ICollection<int> Keys => [];

                    public ICollection<int> Values => [];

                    public int this[int key]
                    {
                        get => key;
                        set => throw new NotSupportedException();
                    }

                    public void Add(int key, int value) => throw new NotSupportedException();

                    public void Add(KeyValuePair<int, int> item) => throw new NotSupportedException();

                    public void Clear() => throw new NotSupportedException();

                    public bool Contains(KeyValuePair<int, int> item) => false;

                    public bool ContainsKey(int key) => key >= 0 && key < entryCount;

                    public void CopyTo(KeyValuePair<int, int>[] array, int arrayIndex) => throw new NotSupportedException();

                    public IEnumerator<KeyValuePair<int, int>> GetEnumerator()
                    {
                        for (var key = 0; key < entryCount; key++)
                        {
                            yield return new KeyValuePair<int, int>(key, key);
                        }
                    }

                    public bool Remove(int key) => throw new NotSupportedException();

                    public bool Remove(KeyValuePair<int, int> item) => throw new NotSupportedException();

                    public bool TryGetValue(int key, out int value)
                    {
                        value = key;
                        return ContainsKey(key);
                    }

                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
            }
            """;
}
