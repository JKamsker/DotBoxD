using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerLiveSettingRangeDefaultSurpriseTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    public void Generator_rejects_live_setting_defaults_outside_declared_range(string defaultValue)
    {
        var result = RunGenerator($$"""
            using System.ComponentModel.DataAnnotations;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message);

            [Plugin("range-default")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                [Range(0, 10)]
                public int Threshold { get; set; } = {{defaultValue}};

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        var diagnostics = result.Diagnostics;
        Assert.True(
            diagnostics.Any(diagnostic => diagnostic.Id == "DBXK100"),
            $"Expected a DBXK100 generator diagnostic for default {defaultValue}; generated {result.GeneratedTrees.Length} tree(s).");
        Assert.Empty(result.GeneratedTrees);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDPluginLiveSettingRangeDefaultTest",
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

        var result = driver.GetRunResult();
        if (result.GeneratedTrees.Length > 0)
        {
            Assert.Empty(diagnostics.Where(IsError));
            Assert.Empty(outputCompilation.GetDiagnostics().Where(IsError));
        }

        return result;
    }

    private static bool IsError(Diagnostic diagnostic)
    {
        return diagnostic.Severity == DiagnosticSeverity.Error;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
