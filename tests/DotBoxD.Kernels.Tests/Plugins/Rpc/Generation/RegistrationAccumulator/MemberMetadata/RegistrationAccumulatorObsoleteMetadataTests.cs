using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorObsoleteMetadataTests
{
    [Fact]
    public void Generated_accumulators_preserve_obsolete_metadata_without_generated_cs0618()
    {
        var result = RunGeneratorWithWarningsAsErrors("""
            using System;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Obsolete("Use CurrentControl")]
            [GeneratePluginRegistrationAccumulator("LegacyRegistrationAccumulator", "RegisterAsync")]
            internal sealed class LegacyControl
            {
                [Obsolete("Use RegisterCurrentAsync")]
                public ValueTask<string> RegisterAsync<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("registered");
            }

            [Obsolete("Use CurrentWorld")]
            [GeneratePluginRegistrationRootAccumulator("LegacyWorldRegistrationAccumulator")]
            internal sealed class LegacyWorld
            {
                public LegacyControl Legacy { get; } = new();
            }
            """);

        var generatedObsoleteDiagnostics = result.OutputCompilation.GetDiagnostics()
            .Where(d => d.Id == "CS0618" && result.IsGeneratedTree(d.Location.SourceTree))
            .Select(d => d.ToString())
            .ToArray();

        Assert.True(
            generatedObsoleteDiagnostics.Length == 0,
            "Generated CS0618 diagnostics leaked:" + Environment.NewLine +
            string.Join(Environment.NewLine, generatedObsoleteDiagnostics));

        var generated = string.Join("\n", result.GeneratedSources);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use CurrentControl\")]",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "internal sealed class LegacyRegistrationAccumulator",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use RegisterCurrentAsync\")]",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "public LegacyRegistrationAccumulator RegisterAsync<TService, TKernel>()",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use CurrentWorld\")]",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "internal sealed class LegacyWorldRegistrationAccumulator",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_accumulators_do_not_emit_malformed_obsolete_attributes_for_unresolved_arguments()
    {
        var result = RunGeneratorWithWarningsAsErrors("""
            using System;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Obsolete("Use CurrentControl", MissingFlag)]
            [GeneratePluginRegistrationAccumulator("LegacyRegistrationAccumulator", "RegisterAsync")]
            internal sealed class LegacyControl
            {
                public ValueTask<string> RegisterAsync<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("registered");
            }
            """);

        var generated = string.Join("\n", result.GeneratedSources);
        Assert.DoesNotContain(
            "[global::System.ObsoleteAttribute(\"Use CurrentControl\"]",
            generated,
            StringComparison.Ordinal);

        var generatedSyntaxDiagnostics = result.OutputCompilation.GetDiagnostics()
            .Where(d => result.IsGeneratedTree(d.Location.SourceTree))
            .Where(d => d.Id is "CS1026" or "CS1001" or "CS1003")
            .Select(d => d.ToString())
            .ToArray();

        Assert.True(
            generatedSyntaxDiagnostics.Length == 0,
            "Generated syntax diagnostics leaked:" + Environment.NewLine +
            string.Join(Environment.NewLine, generatedSyntaxDiagnostics));
    }

    private static GeneratedCompilationResult RunGeneratorWithWarningsAsErrors(string source)
    {
        var inputTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        var compilation = CSharpCompilation.Create(
            "RegistrationAccumulatorObsoleteMetadataTest",
            [inputTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generatedTrees = driver.GetRunResult().GeneratedTrees
            .ToHashSet<SyntaxTree>(ReferenceEqualityComparer.Instance);
        var generatedSources = driver.GetRunResult().GeneratedTrees
            .Select(tree => tree.GetText().ToString())
            .ToArray();
        return new GeneratedCompilationResult(outputCompilation, generatedTrees, generatedSources);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private sealed record GeneratedCompilationResult(
        Compilation OutputCompilation,
        HashSet<SyntaxTree> GeneratedTrees,
        IReadOnlyList<string> GeneratedSources)
    {
        public bool IsGeneratedTree(SyntaxTree? tree)
            => tree is not null && GeneratedTrees.Contains(tree);
    }
}
