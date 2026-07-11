using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class LowerToIrMethodKindValidationSurpriseTests
{
    [Fact]
    public void Invalid_LowerToIrMethod_kind_reports_DBXK100_without_anonymous_package()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.ProbeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """, """
                [LowerToIrMethod((LoweredIrMethodKind)99)]
                public ValueTask<TReturn> ProbeAsync<TReturn>(
                    Func<IGameWorldAccess, ValueTask<TReturn>> lambda,
                    CancellationToken cancellationToken = default)
                    => throw new InvalidOperationException("not lowered");
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));
        var diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Id == "DBXK100" &&
                         candidate.GetMessage().Contains("LowerToIrMethod", StringComparison.Ordinal));

        Assert.Contains("LoweredIrMethodKind", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}
