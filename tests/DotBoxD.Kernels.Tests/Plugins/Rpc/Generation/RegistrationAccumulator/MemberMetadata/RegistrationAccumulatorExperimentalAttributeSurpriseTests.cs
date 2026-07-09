using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorExperimentalAttributeSurpriseTests
{
    private const string DiagnosticId = "DBXEXP_REG";
    private const string ExpectedAttribute =
        "global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP_REG\")";

    [Fact]
    public void Generated_target_accumulator_preserves_experimental_registration_method_contract()
    {
        var run = RunGenerator("""
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "Replace")]
            internal sealed class RemoteServiceControl
            {
                [Experimental("DBXEXP_REG")]
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("service");
            }

            public interface IMonsterService
            {
            }

            public sealed class MonsterKernel : IMonsterService
            {
            }

            internal static class Probe
            {
                public static void Use(RemoteServiceControl services)
                    => new ServiceRegistrationAccumulator(services)
                        .Replace<IMonsterService, MonsterKernel>();
            }
            """);

        if (HasFailClosedExperimentalDiagnostic(run.Diagnostics))
        {
            Assert.Fail("Registration accumulator generation must preserve ExperimentalAttribute rather than fail closed.");
        }

        var source = GeneratedSource(run.Result, "ServiceRegistrationAccumulator");
        Assert.Contains(ExpectedAttribute, source, StringComparison.Ordinal);
        Assert.Empty(GeneratedExperimentalDiagnostics(run));
        Assert.Contains(
            SourceExperimentalDiagnostics(run),
            diagnostic => SourceText(run, diagnostic).Contains(".Replace<IMonsterService", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_accumulators_preserve_experimental_receiver_and_root_type_contracts()
    {
        var run = RunGenerator("""
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Experimental("DBXEXP_REG")]
            [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "Replace")]
            internal sealed class RemoteServiceControl
            {
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("service");
            }

            [Experimental("DBXEXP_REG")]
            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl
            {
                public RemoteMonsterControl Monsters { get; } = new();
            }

            [GeneratePluginRegistrationAccumulator("RemoteMonsterExtensionAccumulator", "Extend")]
            internal sealed class RemoteMonsterControl
            {
                public ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                    => ValueTask.FromResult("extension");
            }
            """);

        if (HasFailClosedExperimentalDiagnostic(run.Diagnostics))
        {
            Assert.Fail("Registration accumulator generation must preserve ExperimentalAttribute rather than fail closed.");
        }

        Assert.Contains(ExpectedAttribute, GeneratedSource(run.Result, "ServiceRegistrationAccumulator"), StringComparison.Ordinal);
        Assert.Contains(ExpectedAttribute, GeneratedSource(run.Result, "WorldRegistrationAccumulator"), StringComparison.Ordinal);
        Assert.Empty(GeneratedExperimentalDiagnostics(run));
    }

    [Fact]
    public void Generated_accumulators_skip_experimental_attribute_with_blank_diagnostic_id()
    {
        var run = RunGenerator("""
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "Replace")]
            internal sealed class RemoteServiceControl
            {
                [Experimental("   ")]
                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => ValueTask.FromResult("service");
            }
            """, allowCompilationErrors: true);

        var source = GeneratedSource(run.Result, "ServiceRegistrationAccumulator");

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "CS9211");
        Assert.DoesNotContain("ExperimentalAttribute", source, StringComparison.Ordinal);
        Assert.Contains("Replace<TService, TKernel>", source, StringComparison.Ordinal);
    }

    private static GeneratorRun RunGenerator(string source, bool allowCompilationErrors = false)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var sourceTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "RegistrationAccumulatorExperimentalAttributeTests",
            [sourceTree],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>
                {
                    [DiagnosticId] = ReportDiagnostic.Error,
                }));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);
        var result = driver.GetRunResult();
        var diagnostics = generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToArray();

        if (!allowCompilationErrors)
        {
            Assert.DoesNotContain(
                diagnostics,
                diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal) &&
                              diagnostic.Severity == DiagnosticSeverity.Error);
        }

        return new GeneratorRun(result, outputCompilation, sourceTree, diagnostics);
    }

    private static bool HasFailClosedExperimentalDiagnostic(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Any(static diagnostic =>
            diagnostic.Id == "DBXK100" &&
            diagnostic.GetMessage().Contains("Experimental", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<Diagnostic> GeneratedExperimentalDiagnostics(GeneratorRun run)
    {
        var generatedTrees = run.Result.GeneratedTrees.ToHashSet();
        return run.OutputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id == DiagnosticId &&
                                 diagnostic.Location.SourceTree is not null &&
                                 generatedTrees.Contains(diagnostic.Location.SourceTree))
            .ToArray();
    }

    private static IReadOnlyList<Diagnostic> SourceExperimentalDiagnostics(GeneratorRun run)
        => run.OutputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id == DiagnosticId &&
                                 diagnostic.Location.SourceTree == run.SourceTree)
            .ToArray();

    private static string SourceText(GeneratorRun run, Diagnostic diagnostic)
    {
        var span = diagnostic.Location.SourceSpan;
        var text = run.SourceTree.GetText().ToString();
        var start = Math.Max(0, span.Start - 40);
        var length = Math.Min(text.Length - start, span.Length + 80);
        return text.Substring(start, length);
    }

    private static string GeneratedSource(GeneratorDriverRunResult result, string hintNameFragment)
        => result.GeneratedTrees
            .Single(tree => tree.FilePath.Contains(hintNameFragment, StringComparison.Ordinal))
            .GetText()
            .ToString();

    private static IEnumerable<MetadataReference> References()
        => TrustedPlatformReferences()
            .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        Compilation OutputCompilation,
        SyntaxTree SourceTree,
        IReadOnlyList<Diagnostic> Diagnostics);
}
