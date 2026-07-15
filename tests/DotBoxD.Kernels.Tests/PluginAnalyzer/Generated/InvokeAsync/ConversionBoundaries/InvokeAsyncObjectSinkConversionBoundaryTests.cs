using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncObjectSinkConversionBoundaryTests
{
    [Fact]
    public void Capture_member_rejects_unsupported_contextual_conversion()
    {
        var result = RunGenerator(UsageSource("""
            public sealed class Capture
            {
                public decimal Value;
            }

            public static ValueTask<int> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    bag.Value = 1;
                    return world.GetHealth("monster-1");
                });
            """));

        AssertUnsupportedSink(result, "capture member 'Value'");
    }

    [Fact]
    public void Capture_member_preserves_supported_widening_conversion()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Capture
            {
                public long Value;
            }

            public static ValueTask<int> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    bag.Value = 1;
                    return world.GetHealth("monster-1");
                });
            """));
        var source = GeneratedSource(result);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_binding_argument_rejects_unsupported_contextual_conversion()
    {
        var result = RunGenerator(UsageSource("""
            [HostBinding("host.sample.write", "sample.write", SandboxEffect.Cpu)]
            private static int Write(decimal value) => throw new InvalidOperationException();

            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return Write(1);
                });
            """));

        AssertUnsupportedSink(result, "parameter 'value'");
    }

    [Fact]
    public void Host_binding_argument_preserves_supported_widening_conversion()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            [HostBinding("host.sample.write", "sample.write", SandboxEffect.Cpu)]
            private static int Write(long value) => throw new InvalidOperationException();

            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return Write(1);
                });
            """));
        var source = GeneratedSource(result);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dto_constructor_argument_rejects_user_defined_contextual_conversion()
    {
        var result = RunGenerator(UsageSource("""
            public sealed record Source(int Value);

            public sealed record Target(long Value)
            {
                public static implicit operator Target(Source source) => new(source.Value);
            }

            public sealed record Envelope(Target Value);

            public static ValueTask<Envelope> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Envelope(new Source(world.GetHealth("monster-1")));
                });
            """));

        AssertUnsupportedSink(result, "constructor for 'Envelope' parameter 'Value'");
    }

    [Fact]
    public void Dto_constructor_argument_preserves_supported_widening_conversion()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed record Envelope(long Value);

            public static ValueTask<Envelope> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Envelope(world.GetHealth("monster-1"));
                });
            """));
        var source = GeneratedSource(result);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dto_initializer_member_rejects_unsupported_contextual_conversion()
    {
        var result = RunGenerator(UsageSource("""
            public sealed record Envelope
            {
                public decimal Value { get; init; }
            }

            public static ValueTask<Envelope> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Envelope { Value = 1 };
                });
            """));

        AssertUnsupportedSink(result, "initializer for 'Envelope' member 'Value'");
    }

    [Fact]
    public void Dto_initializer_member_preserves_supported_widening_conversion()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed record Envelope
            {
                public long Value { get; init; }
            }

            public static ValueTask<Envelope> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return new Envelope { Value = world.GetHealth("monster-1") };
                });
            """));
        var source = GeneratedSource(result);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }

    private static string GeneratedSource(Microsoft.CodeAnalysis.GeneratorDriverRunResult result)
        => string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

    private static void AssertUnsupportedSink(
        Microsoft.CodeAnalysis.GeneratorDriverRunResult result,
        string sink)
        => Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(sink, StringComparison.OrdinalIgnoreCase));
}
