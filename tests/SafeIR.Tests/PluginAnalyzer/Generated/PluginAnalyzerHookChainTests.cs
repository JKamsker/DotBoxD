using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SafeIR;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Phase C lowering: the generator lowers an inline On&lt;TEvent&gt;().Where(lambda).InvokeKernel(lambda)
/// chain into a verified-IR package — the lambda bodies become the module's ShouldHandle/Handle — and
/// fails safe (emits nothing, no SGP100) for shapes outside the supported subset.
/// </summary>
public sealed class PluginAnalyzerHookChainTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Lowers_a_Where_then_InvokeKernel_chain_to_a_package()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Where((e, ctx) => e.Distance <= 5)
                        .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "SGP100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_bare_InvokeKernel_chain_with_no_Where()
    {
        var result = RunGenerator("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record AttackEvent(string AttackerId, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<AttackEvent>()
                        .InvokeKernel((e, ctx) => ctx.Messages.Send(e.AttackerId, "taunt"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "SGP100");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "SafeIrHookChainGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginServer).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SafeIrPluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
