using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiPhysicalFileProviderReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "physical provider metadata probe",
        """
        using var provider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(".");
        return provider.GetFileInfo("plugin.txt").Exists;
        """,
        "Microsoft.Extensions.FileProviders.PhysicalFileProvider")]
    [InlineData(
        "direct System.IO control",
        """return System.IO.File.Exists("plugin.txt");""",
        "System.IO.File")]
    public async Task Reports_host_file_provider_metadata_access_from_event_kernel(
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

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("file-provider-metadata")]
                public sealed class FileProviderMetadataKernel : IEventKernel<string>
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
            "DotBoxDPluginAnalyzerPhysicalFileProviderReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(ReferenceByFileName("Microsoft.Extensions.FileProviders.Abstractions.dll"))
                .Append(ReferenceByFileName("Microsoft.Extensions.FileProviders.Physical.dll")),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference ReferenceByFileName(string fileName)
    {
        var path = TrustedPlatformAssemblyPaths().SingleOrDefault(
            reference => string.Equals(Path.GetFileName(reference), fileName, StringComparison.OrdinalIgnoreCase));
        Assert.False(path is null, $"{fileName} must be available to compile the regression source.");
        return MetadataReference.CreateFromFile(path);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => TrustedPlatformAssemblyPaths().Select(reference => MetadataReference.CreateFromFile(reference));

    private static string[] TrustedPlatformAssemblyPaths()
        => ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
}
