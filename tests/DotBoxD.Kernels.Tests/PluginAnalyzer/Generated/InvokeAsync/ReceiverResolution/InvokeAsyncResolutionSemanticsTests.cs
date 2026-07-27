using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncResolutionSemanticsTests
{
    [Fact]
    public void Ambiguous_custom_call_preserves_first_candidate_diagnostic()
    {
        var result = RunGenerator(UsageSource(
            """
                public static void Run(RemotePluginServer kernels)
                {
                    _ = kernels.ChooseAsync(null!);
                }
            """,
            """
                [LowerToIrMethod((LoweredIrMethodKind)99)]
                public ValueTask<int> ChooseAsync(
                    Func<IGameWorldAccess, ValueTask<int>> lambda)
                    => throw new InvalidOperationException("not lowered");

                [LowerToIrMethod(LoweredIrMethodKind.AnonymousInvocation)]
                public ValueTask<long> ChooseAsync(
                    Func<IGameWorldAccess, ValueTask<long>> lambda)
                    => throw new InvalidOperationException("not lowered");
            """));

        var diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Id == "DBXK100" &&
                         candidate.GetMessage().Contains("LowerToIrMethod", StringComparison.Ordinal));

        Assert.Contains("'99'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
