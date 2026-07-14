using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiTimeRandomReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData("new System.Random().Next(0, 10) >= 0", "System.Random")]
    [InlineData("System.Random.Shared.Next(0, 10) >= 0", "System.Random")]
    [InlineData("System.DateTimeOffset.UtcNow.Year >= 0", "System.DateTimeOffset")]
    [InlineData("System.Guid.NewGuid() != System.Guid.Empty", "System.Guid")]
    public async Task Reports_direct_nondeterministic_time_and_random_api_use(
        string expression,
        string expectedMessageFragment)
    {
        var diagnostics = await AnalyzeAsync(CreateSource("time-random-leak", expression));

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(expectedMessageFragment, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_forbidden_file_call_control()
    {
        const string expression = """System.IO.File.ReadAllText("/x").Length > 0""";

        var diagnostics = await AnalyzeAsync(CreateSource("direct-forbidden-call-control", expression));

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_deterministic_non_host_control()
    {
        const string expression = "System.StringComparer.Ordinal.Equals(e, e)";

        var diagnostics = await AnalyzeAsync(CreateSource("deterministic-control", expression));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string CreateSource(string pluginId, string expression)
    {
        return $$"""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("{{pluginId}}")]
                public sealed class TimeRandomKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return {{expression}};
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
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
            "DotBoxDPluginAnalyzerTimeRandomReachabilityTest",
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
