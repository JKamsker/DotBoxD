using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorMetadataAttributeIdentitySurpriseTests
{
    [Fact]
    public void Foreign_experimental_attribute_does_not_change_generated_accumulator_contract()
    {
        var result = RunGenerator(
            """
            extern alias foreignExperimental;

            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("ForeignRegistrationAccumulator", "Replace")]
            internal sealed class ForeignRegistrationControl
            {
                [foreignExperimental::System.Diagnostics.CodeAnalysis.Experimental("FOREIGN_REG")]
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("foreign");
            }

            [GeneratePluginRegistrationAccumulator("RealRegistrationAccumulator", "Replace")]
            internal sealed class RealRegistrationControl
            {
                [Experimental("REAL_REG")]
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("real");
            }
            """,
            CreateForeignExperimentalReference());

        var foreignSource = GeneratedSource(result, "ForeignRegistrationAccumulator");
        var realSource = GeneratedSource(result, "RealRegistrationAccumulator");

        Assert.DoesNotContain("ExperimentalAttribute", foreignSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FOREIGN_REG", foreignSource, StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"REAL_REG\")",
            realSource,
            StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(string source, MetadataReference foreignReference)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "RegistrationAccumulatorMetadataAttributeIdentityTests",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(foreignReference),
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

    private static MetadataReference CreateForeignExperimentalReference()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignExperimentalAttribute",
            [CSharpSyntaxTree.ParseText(
                """
                namespace System.Diagnostics.CodeAnalysis;

                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class ExperimentalAttribute(string diagnosticId) : System.Attribute
                {
                    public string DiagnosticId { get; } = diagnosticId;
                }
                """)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        return MetadataReference.CreateFromImage(
            assembly.ToArray(),
            new MetadataReferenceProperties(MetadataImageKind.Assembly, aliases: ["foreignExperimental"]));
    }

    private static string GeneratedSource(GeneratorDriverRunResult result, string hintNameFragment)
        => result.GeneratedTrees
            .Single(tree => tree.FilePath.Contains(hintNameFragment, StringComparison.Ordinal))
            .GetText()
            .ToString();

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
