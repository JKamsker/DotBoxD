using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiLicenseManagerReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_licensemanager_lock_context_from_event_kernel()
    {
        const string statement = "LicenseManager.LockContext(e);";
        var source = Source(
            """
            using System.ComponentModel;
            """,
            $$"""
            {{statement}}
            return true;
            """);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.ComponentModel.LicenseManager", diagnostic.GetMessage(), StringComparison.Ordinal);
        AssertDiagnosticLine(source, diagnostic, statement);
    }

    [Fact]
    public async Task Reports_direct_file_access_control_from_event_kernel()
    {
        const string statement = """_ = System.IO.File.ReadAllText("/x");""";
        var source = Source(
            "",
            $$"""
            {{statement}}
            return true;
            """);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
        AssertDiagnosticLine(source, diagnostic, statement);
    }

    [Fact]
    public async Task Does_not_report_benign_componentmodel_value_types()
    {
        var source = Source(
            """
            using System.ComponentModel;
            """,
            """
            var args = new CancelEventArgs(cancel: false);
            return !args.Cancel;
            """);

        var diagnostics = await AnalyzeAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    private static string Source(string extraUsings, string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                {{extraUsings}}

                [Plugin("licensemanager-host-state")]
                public sealed class LicenseManagerKernel : IEventKernel<object>
                {
                    public bool ShouldHandle(object e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(object e, HookContext context) { }
                }
            }
            """;

    private static void AssertDiagnosticLine(string source, Diagnostic diagnostic, string expectedLine)
    {
        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        var actualLine = source.Split('\n')[position.Line].Trim();
        Assert.Equal(expectedLine, actualLine);
    }

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
            "DotBoxDPluginAnalyzerLicenseManagerReachabilityTest",
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
