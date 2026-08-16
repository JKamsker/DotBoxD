namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiImmutableSortedSetBuilderUnionWithReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_immutable_sorted_set_builder_union_with_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerImmutableSortedSetBuilderUnionWithReachabilityTest");

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK001" &&
                diagnostic.GetMessage().Contains("System.IO.File", StringComparison.Ordinal));

        var diagnostic = Assert.Single(diagnostics.Where(
            diagnostic => diagnostic.Id == "DBXK001" &&
                diagnostic.GetMessage().Contains(
                    "System.Collections.Immutable.ImmutableSortedSet<T>.Builder.UnionWith",
                    StringComparison.Ordinal)));

        Assert.Contains(
            "System.Collections.Immutable.ImmutableSortedSet<T>.Builder.UnionWith",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Immutable;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("immutable-sorted-set-builder-union-with")]
                public sealed class ImmutableSortedSetBuilderUnionWithKernel : IEventKernel<string>
                {
                    private static readonly ImmutableSortedSet<byte>.Builder Retained = CreateRetained();
                    private static readonly bool FileControl = System.IO.File.Exists("plugin-immutable-sorted-set.txt");

                    private static ImmutableSortedSet<byte>.Builder CreateRetained()
                    {
                        var builder = ImmutableSortedSet.CreateBuilder<byte>();
                        builder.UnionWith(Enumerable.Repeat((byte)0, int.MaxValue));
                        return builder;
                    }

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
