namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiImmutableHashSetBuilderUnionWithReachabilityTests
{
    [Theory]
    [InlineData(
        "ImmutableHashSet<byte>.Builder UnionWith",
        "private static readonly ImmutableHashSet<byte>.Builder Retained = CreateRetainedBuilder();",
        """
        private static ImmutableHashSet<byte>.Builder CreateRetainedBuilder()
        {
            var builder = ImmutableHashSet.CreateBuilder<byte>();
            builder.UnionWith(Enumerable.Repeat((byte)0, int.MaxValue));
            return builder;
        }
        """,
        "System.Collections.Immutable.ImmutableHashSet<T>.Builder.UnionWith")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin-immutable-hash-set.txt\");",
        "",
        "System.IO.File")]
    public async Task Reports_unbounded_immutable_hash_set_builder_union_with_static_initializer(
        string testCase,
        string staticMember,
        string helperMember,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(staticMember, helperMember),
            "DotBoxDPluginAnalyzerImmutableHashSetBuilderUnionWithReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    private static string Source(string staticMember, string helperMember)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Immutable;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("immutable-hash-set-builder-union-with")]
                public sealed class ImmutableHashSetBuilderUnionWithKernel : IEventKernel<string>
                {
                    {{staticMember}}

                    {{helperMember}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
