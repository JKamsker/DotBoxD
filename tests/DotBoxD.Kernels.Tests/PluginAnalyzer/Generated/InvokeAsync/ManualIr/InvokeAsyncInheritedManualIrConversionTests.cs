using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGeneratedReceiverTestSources;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncInheritedManualIrConversionTests
{
    [Fact]
    public void Derived_carrier_with_inherited_implicit_conversion_preserves_manual_ir_path()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public abstract class ManualIrCarrier
                {
                    public static implicit operator IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>(
                        ManualIrCarrier _)
                        => IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>.FromGenerated(
                            "manual",
                            static () => new object(),
                            static _ => Array.Empty<byte>(),
                            static (_, _) => 0);
                }

                public sealed class DerivedManualIrCarrier : ManualIrCarrier;

                public ValueTask<int> Probe(DerivedManualIrCarrier irInvocation)
                    => InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        },
                        irInvocation);
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}
