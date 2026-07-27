using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class GenericConstructionWorklistTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        nameof(GenericConstructionWorklistSources.SelfCycle),
        "_ = GenericFactory.Create<DangerousConstructed>(true);")]
    [InlineData(nameof(GenericConstructionWorklistSources.Diamond), "GenericFactory.Create<DangerousConstructed>();")]
    [InlineData(
        nameof(GenericConstructionWorklistSources.TwoSlotPermutation),
        "GenericFactory.Create<SafeConstructed, DangerousConstructed>();")]
    [InlineData(
        nameof(GenericConstructionWorklistSources.ContainingTypeCycle),
        "new GenericFactory<DangerousConstructed>().Create(true);")]
    public async Task Reports_forbidden_constructor_through_fixed_point_shape(
        string sourceName,
        string expectedCall)
    {
        var source = Source(sourceName);
        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(static item => item.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        var line = diagnostic.Location.SourceTree!.GetText().Lines[position.Line].ToString().Trim();
        Assert.Equal(expectedCall, line);
    }

    private static string Source(string name)
        => name switch
        {
            nameof(GenericConstructionWorklistSources.SelfCycle) => GenericConstructionWorklistSources.SelfCycle,
            nameof(GenericConstructionWorklistSources.Diamond) => GenericConstructionWorklistSources.Diamond,
            nameof(GenericConstructionWorklistSources.TwoSlotPermutation) =>
                GenericConstructionWorklistSources.TwoSlotPermutation,
            nameof(GenericConstructionWorklistSources.ContainingTypeCycle) =>
                GenericConstructionWorklistSources.ContainingTypeCycle,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDGenericConstructionWorklistTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == "AD0001");
        return diagnostics;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(static reference => MetadataReference.CreateFromFile(reference));
    }
}
