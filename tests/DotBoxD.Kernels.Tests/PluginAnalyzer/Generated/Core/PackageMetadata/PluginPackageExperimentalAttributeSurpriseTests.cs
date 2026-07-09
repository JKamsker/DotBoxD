using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginPackageExperimentalAttributeSurpriseTests
{
    private const string ConsumerPath = "GeneratedPackageConsumer.cs";
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_package_preserves_experimental_kernel_contract()
    {
        var compilation = CreateCompilation("""
            using System.Diagnostics.CodeAnalysis;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Experimental("DBXEXP_PACKAGE")]
            [Plugin("sample.experimental")]
            public sealed partial class ExperimentalDamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "experimental");
            }
            """);
        var driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var generatorErrors = generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (generatorErrors.Any(d => d.Id.StartsWith("DBXK", StringComparison.Ordinal)))
        {
            return;
        }

        Assert.Empty(generatorErrors);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = Assert.Single(PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult()).GeneratedTrees).GetText().ToString();
        Assert.Contains(
            "global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP_PACKAGE\")",
            generated,
            StringComparison.Ordinal);

        var consumerCompilation = outputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            """
            namespace Consumer;

            public static class GeneratedPackageConsumer
            {
                public static object Create()
                    => Sample.ExperimentalDamagePluginPackage.Create();
            }
            """,
            ParseOptions,
            path: ConsumerPath));
        var consumerDiagnostics = consumerCompilation.GetDiagnostics();

        Assert.Contains(
            consumerDiagnostics,
            d => d.Id == "DBXEXP_PACKAGE" &&
                 d.Location.GetLineSpan().Path == ConsumerPath);
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDExperimentalPackageTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
