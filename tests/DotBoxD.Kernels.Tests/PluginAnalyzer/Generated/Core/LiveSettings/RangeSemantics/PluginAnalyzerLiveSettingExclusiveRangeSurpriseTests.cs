using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerLiveSettingExclusiveRangeSurpriseTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData("Range(0, 10)", "0")]
    [InlineData("Range(0, 10)", "10")]
    public void Generator_accepts_inclusive_range_boundary_defaults(string rangeAttribute, string defaultValue)
    {
        var result = RunGenerator(Source(rangeAttribute, defaultValue));

        var generated = Assert.Single(result.GeneratedTrees).GetText().ToString();
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains(
            $"new global::DotBoxD.Plugins.LiveSettingDefinition(\"Threshold\", \"int\", {defaultValue}, 0, 10)",
            generated);
    }

    [Theory]
    [InlineData("Range(0, 10, MinimumIsExclusive = true)", "0")]
    [InlineData("Range(0, 10, MaximumIsExclusive = true)", "10")]
    public void Generator_rejects_exclusive_range_boundary_defaults(string rangeAttribute, string defaultValue)
    {
        var result = RunGenerator(Source(rangeAttribute, defaultValue));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.GetMessage().Contains("exclusive", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.GeneratedTrees);
    }

    private static string Source(string rangeAttribute, string defaultValue)
        => $$"""
            using System.ComponentModel.DataAnnotations;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            [Plugin("exclusive-range")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                [{{rangeAttribute}}]
                public int Threshold { get; set; } = {{defaultValue}};

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """;

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginLiveSettingExclusiveRangeTest",
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
            out var diagnostics);

        var result = PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult());
        if (result.GeneratedTrees.Length > 0)
        {
            Assert.Empty(diagnostics.Where(IsError));
            Assert.Empty(outputCompilation.GetDiagnostics().Where(IsError));
        }

        return result;
    }

    private static bool IsError(Diagnostic diagnostic)
        => diagnostic.Severity == DiagnosticSeverity.Error;

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
