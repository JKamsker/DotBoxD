using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultFileLocalRegressionTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    [Fact]
    public void HookResult_rejects_file_local_result_types_before_emitting_sources()
    {
        const string source = """
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Regression.Game;

            [Hook("combat.damage", typeof(FileDamageResult))]
            public sealed record DamageCtx(int Amount);

            [HookResult]
            file readonly partial record struct FileDamageResult(
                bool Success,
                string? Reason,
                int Amount) : IHookResult;

            public static class GameHooks
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .RegisterLocal(
                            (ctx, hookContext) => new FileDamageResult(true, null, ctx.Amount),
                            priority: 25);
            }
            """;

        var (result, diagnostics) = RunGeneratorCompilation(source);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains("file-local", StringComparison.OrdinalIgnoreCase) &&
                diagnostic.GetMessage().Contains("FileDamageResult", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id is "CS0535" or "CS0117" or "CS0315");

        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain("HookResultBuilders.g.cs", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IRKernel.FromPackage", generated, StringComparison.Ordinal);
    }

    private static (GeneratorDriverRunResult Result, IReadOnlyList<Diagnostic> Diagnostics) RunGeneratorCompilation(
        string source)
    {
        var compilation = CSharpCompilation.Create(
            "HookResultFileLocalRegression",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var diagnostics = generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
            .ToArray();
        return (PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult()), diagnostics);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
