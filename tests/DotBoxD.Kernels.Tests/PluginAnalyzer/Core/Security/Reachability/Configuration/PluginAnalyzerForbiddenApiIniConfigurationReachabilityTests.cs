using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiIniConfigurationReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "private static readonly Microsoft.Extensions.Configuration.IConfiguration Settings = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddIniFile(\"plugin.ini\", optional: true, reloadOnChange: false).Build();",
        "Settings[\"mode\"] is not null",
        "Microsoft.Extensions.Configuration.Ini")]
    [InlineData(
        "private static readonly bool FileExists = System.IO.File.Exists(\"plugin.ini\");",
        "FileExists",
        "System.IO.File")]
    public async Task Reports_configuration_file_providers_reached_from_static_initializers(
        string fieldInitializer,
        string predicate,
        string expectedForbiddenApi)
    {
        var source = Source(fieldInitializer, predicate);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains(expectedForbiddenApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string fieldInitializer, string predicate)
        => $$"""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Configuration;

                [Plugin("ini-configuration-host-file")]
                public sealed class IniConfigurationKernel : IEventKernel<string>
                {
                    {{fieldInitializer}}

                    public bool ShouldHandle(string e, HookContext context) => {{predicate}};

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
            "DotBoxDPluginAnalyzerIniConfigurationReachabilityTest",
            [syntaxTree],
            CompilationReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> CompilationReferences()
    {
        var references = TrustedPlatformReferencePaths()
            .GroupBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references.Values)
        {
            yield return MetadataReference.CreateFromFile(reference);
        }

        foreach (var fileName in AspNetCoreReferenceNames())
        {
            if (!references.ContainsKey(fileName))
            {
                yield return MetadataReference.CreateFromFile(AspNetCoreTestReferences.FindAssembly(fileName));
            }
        }

        yield return MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location);
    }

    private static IEnumerable<string> TrustedPlatformReferencePaths()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references;
    }

    private static string[] AspNetCoreReferenceNames()
        =>
        [
            "Microsoft.Extensions.Configuration.Abstractions.dll",
            "Microsoft.Extensions.Configuration.dll",
            "Microsoft.Extensions.Configuration.FileExtensions.dll",
            "Microsoft.Extensions.Configuration.Ini.dll",
            "Microsoft.Extensions.FileProviders.Abstractions.dll",
            "Microsoft.Extensions.FileProviders.Physical.dll",
            "Microsoft.Extensions.Primitives.dll",
        ];

}
