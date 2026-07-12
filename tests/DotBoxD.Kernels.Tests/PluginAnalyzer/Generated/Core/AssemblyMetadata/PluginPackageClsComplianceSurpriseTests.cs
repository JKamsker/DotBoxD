using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginPackageClsComplianceSurpriseTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_event_plugin_package_does_not_emit_cls_warnings()
    {
        var compilation = CreateCompilation("""
            using System;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            [assembly: CLSCompliant(true)]

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [Plugin("cls-plain")]
            internal sealed partial class PlainKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext context) => true;

                public void Handle(DamageEvent e, HookContext context)
                    => context.Messages.Send(e.TargetId, "matched");
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var runResult = PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult());
        var failClosed = generatorDiagnostics.FirstOrDefault(
            diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("CLS", StringComparison.OrdinalIgnoreCase));
        if (failClosed is not null)
        {
            Assert.Equal(DiagnosticSeverity.Error, failClosed.Severity);
            Assert.Empty(runResult.GeneratedTrees);
            return;
        }

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Single(runResult.GeneratedTrees);

        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Id is "CS3001" or "CS3002" or "CS3003" &&
                          diagnostic.Location.SourceTree is not null);
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDClsPluginPackageTest",
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
