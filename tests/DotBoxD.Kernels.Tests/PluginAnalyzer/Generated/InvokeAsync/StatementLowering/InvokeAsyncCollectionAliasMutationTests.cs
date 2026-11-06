using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

/// <summary>
/// Kernel <c>list.add</c>/<c>map.set</c> store a new collection value back into the mutated local, so CLR
/// reference aliases (another local, a DTO built from the collection) would keep the stale value. InvokeAsync
/// bodies must reject those shapes instead of silently diverging from C#.
/// </summary>
public sealed class InvokeAsyncCollectionAliasMutationTests
{
    [Fact]
    public void List_mutation_through_local_alias_reports_DBXK100()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var values = new System.Collections.Generic.List<int>();
                    var alias = values;
                    alias.Add(world.GetHealth("monster-1"));
                    return values.Count;
                });
            """));

        AssertAliasMutationRejected(result);
    }

    [Fact]
    public void List_mutation_after_building_dto_from_it_reports_DBXK100()
    {
        var result = RunGenerator(UsageSource("""
            public sealed record Holder(System.Collections.Generic.List<int> Values);

            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var values = new System.Collections.Generic.List<int>();
                    var holder = new Holder(values);
                    values.Add(world.GetHealth("monster-1"));
                    return holder.Values.Count;
                });
            """));

        AssertAliasMutationRejected(result);
    }

    [Fact]
    public void Map_index_assignment_through_local_alias_reports_DBXK100()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<System.Collections.Generic.Dictionary<string, int>> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var scores = new System.Collections.Generic.Dictionary<string, int>();
                    var view = scores;
                    scores["monster-1"] = world.GetHealth("monster-1");
                    return view;
                });
            """));

        AssertAliasMutationRejected(result);
    }

    [Fact]
    public void Enumerating_list_while_mutating_it_reports_DBXK100()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var values = new System.Collections.Generic.List<int>();
                    values.Add(world.GetHealth("monster-1"));
                    foreach (var value in values)
                    {
                        values.Add(value);
                    }

                    return values.Count;
                });
            """));

        AssertAliasMutationRejected(result);
    }

    [Fact]
    public void Unaliased_list_built_enumerated_and_returned_still_generates()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<System.Collections.Generic.List<int>> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var values = new System.Collections.Generic.List<int>();
                    values.Add(world.GetHealth("monster-1"));
                    var total = 0;
                    foreach (var value in values)
                    {
                        total += value;
                    }

                    values.Add(total);
                    return values;
                });
            """));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    private static void AssertAliasMutationRejected(GeneratorDriverRunResult result)
        => Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("list/map mutation", StringComparison.Ordinal));
}
