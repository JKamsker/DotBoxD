using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiX509HostIoReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "certificate path constructor",
        """
        using var certificate =
            new System.Security.Cryptography.X509Certificates.X509Certificate2(e);
        return certificate.Subject.Length >= 0;
        """,
        "System.Security.Cryptography.X509Certificates")]
    [InlineData(
        "online chain build",
        """
        using var certificate =
            new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Array.Empty<byte>());
        using var chain = new System.Security.Cryptography.X509Certificates.X509Chain();
        chain.ChainPolicy.RevocationMode =
            System.Security.Cryptography.X509Certificates.X509RevocationMode.Online;
        return chain.Build(certificate);
        """,
        "System.Security.Cryptography.X509Certificates")]
    [InlineData(
        "direct System.IO control",
        "return System.IO.File.Exists(e);",
        "System.IO.File")]
    public async Task Reports_x509_host_io_apis_in_event_kernels(
        string testCase,
        string shouldHandleBody,
        string expectedApi)
    {
        var source = Source(shouldHandleBody);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    [Fact]
    public async Task Allows_in_memory_certificate_parsing()
    {
        var source = Source(
            """
            using var certificate =
                new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Array.Empty<byte>());
            return certificate.RawData.Length >= 0;
            """);

        var diagnostics = await AnalyzeAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    [Fact]
    public async Task Allows_in_memory_certificate_parsing_with_password()
    {
        var source = Source(
            """
            using var certificate =
                new System.Security.Cryptography.X509Certificates.X509Certificate2(
                    System.Array.Empty<byte>(),
                    "password");
            return certificate.RawData.Length >= 0;
            """);

        var diagnostics = await AnalyzeAsync(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK001" &&
                diagnostic.GetMessage().Contains("X509Certificate", StringComparison.Ordinal));
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("x509-host-io")]
                public sealed class X509HostIoKernel : IEventKernel<string>
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
            "DotBoxDPluginAnalyzerX509HostIoReachabilityTest",
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
