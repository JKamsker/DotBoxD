using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiLocalizationReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_resource_manager_string_localizer_from_static_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Localization;
                using Microsoft.Extensions.Logging.Abstractions;
                using Microsoft.Extensions.Options;

                [Plugin("localized-resources")]
                public sealed class LocalizedResourceKernel : IEventKernel<string>
                {
                    private static readonly IStringLocalizer Localizer = CreateLocalizer();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return Localizer["Greeting"].Value.Length >= 0 && e.Length >= 0;
                    }

                    public void Handle(string e, HookContext context) { }

                    private static IStringLocalizer CreateLocalizer()
                    {
                        var factory = new ResourceManagerStringLocalizerFactory(
                            Options.Create(new LocalizationOptions()),
                            NullLoggerFactory.Instance);

                        return factory.Create("Messages", "Host.Assembly");
                    }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains("Microsoft.Extensions.Localization", StringComparison.Ordinal) ||
            message.Contains("ResourceManagerStringLocalizerFactory", StringComparison.Ordinal),
            message);
    }

    [Fact]
    public async Task Reports_file_positive_control_from_static_initializer()
    {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("static-file-control")]
                public sealed class StaticFileKernel : IEventKernel<string>
                {
                    private static readonly bool Exists = System.IO.File.Exists("plugin.txt");

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return Exists || e.Length > 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
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
            "DotBoxDPluginAnalyzerLocalizationReachabilityTest",
            [syntaxTree],
            MetadataReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> MetadataReferences()
    {
        var paths = TrustedPlatformReferencePaths();
        var seenFileNames = new HashSet<string>(
            paths.Select(Path.GetFileName).OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            yield return MetadataReference.CreateFromFile(path);
        }

        foreach (var path in AspNetCoreTestReferences.AssemblyPaths("Microsoft.Extensions.*.dll"))
        {
            if (seenFileNames.Add(Path.GetFileName(path)))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }
    }

    private static IEnumerable<string> TrustedPlatformReferencePaths()
        => ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
}
