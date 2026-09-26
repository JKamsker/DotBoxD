using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorCodeRequirementAttributeSurpriseTests
{
    private const string RequiresUnreferencedCode =
        "[global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute(\"Trimming contract\", Url = \"https://example.test/trimming\")]";

    private const string RequiresDynamicCode =
        "[global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute(\"Dynamic-code contract\", Url = \"https://example.test/dynamic-code\")]";

    private const string RequiresAssemblyFiles =
        "[global::System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute(\"Assembly-files contract\", Url = \"https://example.test/assembly-files\")]";

    [Fact]
    public void Generated_accumulators_preserve_genuine_code_requirement_contracts()
    {
        var result = RunGenerator("""
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Lookalike
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                internal sealed class RequiresUnreferencedCodeAttribute(string message) : Attribute;

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                internal sealed class RequiresDynamicCodeAttribute(string message) : Attribute;

                [AttributeUsage(AttributeTargets.Method)]
                internal sealed class RequiresAssemblyFilesAttribute(string message) : Attribute;
            }

            namespace Sample
            {

            [RequiresUnreferencedCode("Trimming contract", Url = "https://example.test/trimming")]
            [RequiresDynamicCode("Dynamic-code contract", Url = "https://example.test/dynamic-code")]
            [GeneratePluginRegistrationAccumulator("AnnotatedRegistrationAccumulator", "Replace")]
            internal sealed class AnnotatedRegistrationControl
            {
                [RequiresUnreferencedCode("Trimming contract", Url = "https://example.test/trimming")]
                [RequiresDynamicCode("Dynamic-code contract", Url = "https://example.test/dynamic-code")]
                [RequiresAssemblyFiles("Assembly-files contract", Url = "https://example.test/assembly-files")]
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("registered");
            }

            [GeneratePluginRegistrationAccumulator("PortableRegistrationAccumulator", "Replace")]
            internal sealed class PortableRegistrationControl
            {
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("registered");
            }

            [Lookalike.RequiresUnreferencedCode("Foreign trimming contract")]
            [Lookalike.RequiresDynamicCode("Foreign dynamic-code contract")]
            [GeneratePluginRegistrationAccumulator("LookalikeRegistrationAccumulator", "Replace")]
            internal sealed class LookalikeRegistrationControl
            {
                [Lookalike.RequiresUnreferencedCode("Foreign trimming contract")]
                [Lookalike.RequiresDynamicCode("Foreign dynamic-code contract")]
                [Lookalike.RequiresAssemblyFiles("Foreign assembly-files contract")]
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("registered");
            }
            }
            """);

        var annotatedSource = GeneratedSource(result, "AnnotatedRegistrationAccumulator");
        Assert.Contains(
            RequiresDynamicCode + "\n" + RequiresUnreferencedCode +
            "\ninternal sealed class AnnotatedRegistrationAccumulator",
            NormalizeLineEndings(annotatedSource),
            StringComparison.Ordinal);
        Assert.Contains(
            "    " + RequiresAssemblyFiles + "\n    " + RequiresDynamicCode + "\n    " + RequiresUnreferencedCode +
            "\n    public AnnotatedRegistrationAccumulator Replace<TService, TKernel>()",
            NormalizeLineEndings(annotatedSource),
            StringComparison.Ordinal);

        var portableSource = GeneratedSource(result, "PortableRegistrationAccumulator");
        Assert.DoesNotContain("RequiresUnreferencedCodeAttribute", portableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiresDynamicCodeAttribute", portableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiresAssemblyFilesAttribute", portableSource, StringComparison.Ordinal);

        var lookalikeSource = GeneratedSource(result, "LookalikeRegistrationAccumulator");
        Assert.DoesNotContain("Foreign trimming contract", lookalikeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreign dynamic-code contract", lookalikeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreign assembly-files contract", lookalikeSource, StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "RegistrationAccumulatorCodeRequirementAttributeTests",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return driver.GetRunResult();
    }

    private static string GeneratedSource(GeneratorDriverRunResult result, string hintNameFragment)
        => result.GeneratedTrees
            .Single(tree => tree.FilePath.Contains(hintNameFragment, StringComparison.Ordinal))
            .GetText()
            .ToString();

    private static string NormalizeLineEndings(string source)
        => source.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
