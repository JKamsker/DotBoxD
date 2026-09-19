using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorPlatformCompatibilitySurpriseTests
{
    private const string SupportedWindowsAttribute =
        "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]";

    [Fact]
    public void Generated_accumulator_preserves_platform_compatibility_on_control_type_and_registration_method()
    {
        var result = RunGenerator("""
            using System.Runtime.Versioning;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [SupportedOSPlatform("windows")]
            [GeneratePluginRegistrationAccumulator("WindowsRegistrationAccumulator", "Replace")]
            internal sealed class WindowsRegistrationControl
            {
                [SupportedOSPlatform("windows")]
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
            """);

        var windowsSource = GeneratedSource(result, "WindowsRegistrationAccumulator");
        Assert.Contains(
            SupportedWindowsAttribute + "\ninternal sealed class WindowsRegistrationAccumulator",
            windowsSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "    " + SupportedWindowsAttribute +
            "\n    public WindowsRegistrationAccumulator Replace<TService, TKernel>()",
            windowsSource,
            StringComparison.Ordinal);

        var portableSource = GeneratedSource(result, "PortableRegistrationAccumulator");
        Assert.DoesNotContain(SupportedWindowsAttribute, portableSource, StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "RegistrationAccumulatorPlatformCompatibilityTests",
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

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
