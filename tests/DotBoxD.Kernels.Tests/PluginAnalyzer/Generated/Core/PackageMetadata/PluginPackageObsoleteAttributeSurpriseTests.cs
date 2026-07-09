using System.Collections.Immutable;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginPackageObsoleteAttributeSurpriseTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public void Generated_plugin_package_preserves_obsolete_kernel_contract()
    {
        var compilation = CreateCompilation("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(int Amount, string TargetId);

            [Obsolete("Use NewDamageKernel", error: true)]
            [Plugin("legacy-damage")]
            public sealed partial class LegacyDamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => e.Amount > 0;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "legacy damage");
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        if (TryAssertFailClosed(generatorDiagnostics, PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult())))
        {
            return;
        }

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = Assert.Single(PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult()).GeneratedTrees).GetText().ToString();
        Assert.Contains("public static class LegacyDamagePluginPackage", generated, StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use NewDamageKernel\"",
            generated,
            StringComparison.Ordinal);

        var consumerDiagnostics = outputCompilation
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
                namespace Sample.Consumer;

                internal static class DirectGeneratedPackageUse
                {
                    public static void CreatePackage()
                        => Sample.LegacyDamagePluginPackage.Create();
                }
                """, ParseOptions))
            .GetDiagnostics();

        Assert.Contains(
            consumerDiagnostics,
            diagnostic => diagnostic.Id == "CS0619" &&
                          diagnostic.Severity == DiagnosticSeverity.Error &&
                          diagnostic.GetMessage().Contains("Use NewDamageKernel", StringComparison.Ordinal));
    }

    private static bool TryAssertFailClosed(
        ImmutableArray<Diagnostic> generatorDiagnostics,
        GeneratorDriverRunResult runResult)
    {
        var failClosed = generatorDiagnostics.FirstOrDefault(
            diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("LegacyDamageKernel", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("Obsolete", StringComparison.OrdinalIgnoreCase));
        if (failClosed is null)
        {
            return false;
        }

        Assert.Equal(DiagnosticSeverity.Error, failClosed.Severity);
        Assert.Empty(runResult.GeneratedTrees);
        return true;
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDGeneratedPackageObsoleteTest",
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
