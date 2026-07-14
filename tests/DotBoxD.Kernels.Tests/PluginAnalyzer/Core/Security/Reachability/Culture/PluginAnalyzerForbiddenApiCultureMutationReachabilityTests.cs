using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiCultureMutationReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(\"tr-TR\");",
        "CurrentCulture")]
    [InlineData(
        "CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;",
        "DefaultThreadCurrentCulture")]
    public async Task Reports_direct_ambient_culture_mutation_in_event_kernel(
        string statement,
        string expectedMember)
    {
        var diagnostics = await AnalyzeAsync(CreateCultureMutationSource(statement));

        var forbiddenDiagnostics = diagnostics.Where(IsForbiddenHostApi).ToArray();
        Assert.NotEmpty(forbiddenDiagnostics);
        Assert.Contains(
            forbiddenDiagnostics,
            diagnostic =>
                diagnostic.GetMessage().Contains("System.Globalization.CultureInfo", StringComparison.Ordinal)
                && SourceLine(diagnostic).Contains(expectedMember, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reports_direct_file_api_control()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("file-control")]
                public sealed class FileControlKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Where(IsForbiddenHostApi));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_helper_ambient_culture_mutation_in_event_kernel()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using System.Globalization;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("helper-culture-mutation")]
                public sealed class HelperCultureKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        CultureHelper.MutateCulture();
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }

                internal static class CultureHelper
                {
                    public static void MutateCulture()
                    {
                        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                    }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Where(IsForbiddenHostApi));
        Assert.Contains("System.Globalization.CultureInfo", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("MutateCulture", SourceLine(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_ambient_culture_read_in_event_kernel()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using System.Globalization;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("culture-read")]
                public sealed class CultureReadKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return CultureInfo.CurrentCulture.Name.Length >= 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        Assert.Empty(diagnostics.Where(IsForbiddenHostApi));
    }

    private static string CreateCultureMutationSource(string statement)
    {
        return $$"""
            namespace Sample
            {
                using System.Globalization;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("culture-mutation")]
                public sealed class CultureKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{statement}}
                        return string.Compare("i", "I", ignoreCase: true) == 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
    }

    private static bool IsForbiddenHostApi(Diagnostic diagnostic)
        => diagnostic.Id == "DBXK001";

    private static string SourceLine(Diagnostic diagnostic)
    {
        var linePosition = diagnostic.Location.GetLineSpan().StartLinePosition;
        return diagnostic.Location.SourceTree?.ToString().Split('\n')[linePosition.Line].Trim() ?? string.Empty;
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, "Source.cs");
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerCultureMutationReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
