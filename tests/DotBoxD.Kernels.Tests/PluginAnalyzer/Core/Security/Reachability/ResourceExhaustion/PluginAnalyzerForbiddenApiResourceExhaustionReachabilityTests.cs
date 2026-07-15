using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiResourceExhaustionReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        """
        _ = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "password",
            new byte[16],
            int.MaxValue,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            32);
        """,
        "System.Security.Cryptography.Rfc2898DeriveBytes")]
    [InlineData("""_ = System.IO.File.ReadAllText("/x");""", "System.IO.File")]
    public async Task Reports_unmetered_resource_exhaustion_apis_from_event_kernel(
        string statement,
        string expectedApiName)
    {
        var source = CreateSource(statement);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains(expectedApiName, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string CreateSource(string statement)
        => $$"""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("resource-exhaustion-leak")]
                public sealed class ResourceExhaustionKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{statement}}
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, "Source.cs");
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerResourceExhaustionReachabilityTest",
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
