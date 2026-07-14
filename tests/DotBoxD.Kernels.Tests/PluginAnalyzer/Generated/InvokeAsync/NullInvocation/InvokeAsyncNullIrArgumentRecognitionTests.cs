using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGeneratedReceiverTestSources;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncNullIrArgumentRecognitionTests
{
    [Fact]
    public void Non_null_ir_argument_preserves_manual_ir_path()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public ValueTask<int> Probe(
                    IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int> irInvocation)
                    => InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        },
                        irInvocation: irInvocation);
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Converted_default_carrier_preserves_manual_ir_path()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public readonly struct ManualIrCarrier
                {
                    public static explicit operator IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>(
                        ManualIrCarrier _)
                        => IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>.FromGenerated(
                            "manual",
                            static () => new object(),
                            static _ => Array.Empty<byte>(),
                            static (_, _) => 0);
                }

                public ValueTask<int> Probe()
                    => InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        },
                        irInvocation: (IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>)default(ManualIrCarrier));
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("default(ManualIrCarrier)")]
    [InlineData("irInvocation: default(ManualIrCarrier)")]
    public void Implicitly_converted_default_carrier_preserves_manual_ir_path(string irArgument)
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource($$"""
                public readonly struct ManualIrCarrier
                {
                    public static implicit operator IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>(
                        ManualIrCarrier _)
                        => IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>.FromGenerated(
                            "manual",
                            static () => new object(),
                            static _ => Array.Empty<byte>(),
                            static (_, _) => 0);
                }

                public ValueTask<int> Probe()
                    => InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        },
                        {{irArgument}});
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(null)")]
    [InlineData("null!")]
    [InlineData("(IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>?)null")]
    [InlineData("(IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>?)default")]
    [InlineData("null as IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>")]
    public void Null_like_ir_argument_still_generates_InvokeAsync_interceptor(string irExpression)
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource($$"""
                public ValueTask<int> Probe()
                    => InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        },
                        irInvocation: {{irExpression}});
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Ir_invocation_capture_is_not_mistaken_for_the_manual_ir_argument()
    {
        var result = RunGenerator(GeneratedFacadeBodySource("""
                public ValueTask<int> Probe(
                    IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int> captures)
                    => InvokeAsync(
                        captures,
                        async (
                            IGameWorldAccess world,
                            IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int> bag) =>
                        {
                            return world.GetHealth("monster-1");
                        });
            """));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }
}
