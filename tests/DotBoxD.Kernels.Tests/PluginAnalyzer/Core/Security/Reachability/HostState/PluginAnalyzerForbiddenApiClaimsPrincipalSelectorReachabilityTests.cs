using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiClaimsPrincipalSelectorReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "claims principal selector",
        "System.Security.Claims.ClaimsPrincipal.ClaimsPrincipalSelector = () => new System.Security.Claims.ClaimsPrincipal();")]
    [InlineData(
        "primary identity selector",
        """
        System.Security.Claims.ClaimsPrincipal.PrimaryIdentitySelector = identities =>
        {
            foreach (var identity in identities)
            {
                return identity;
            }

            return null;
        };
        """)]
    public async Task Reports_claims_principal_selector_mutation_from_event_kernels(
        string testCase,
        string statement)
    {
        var diagnostics = await AnalyzeAsync(Source($$"""
            {{statement}}
            return true;
            """));

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains("System.Security.Claims.ClaimsPrincipal", StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    [Fact]
    public async Task Reports_known_forbidden_file_api_control()
    {
        var diagnostics = await AnalyzeAsync(Source("return System.IO.File.Exists(e);"));

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_benign_claims_principal_construction()
    {
        var diagnostics = await AnalyzeAsync(Source("""
            var principal = new System.Security.Claims.ClaimsPrincipal();
            return principal is not null;
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("claims-principal-selector-leak")]
                public sealed class ClaimsPrincipalSelectorKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var compilerErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilerErrors);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerClaimsPrincipalSelectorReachabilityTest",
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
