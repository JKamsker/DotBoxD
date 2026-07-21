using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEventSourceLoggingReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        """
        private static readonly bool Initialized = InitializeLogging();

        private static bool InitializeLogging()
        {
            using var factory = LoggerFactory.Create(builder => builder.AddEventSourceLogger());
            factory.CreateLogger("plugin").LogWarning("plugin event");
            return true;
        }
        """,
        "Initialized",
        "Microsoft.Extensions.Logging.EventSource")]
    [InlineData(
        """
        private static readonly bool FileExists = System.IO.File.Exists("plugin.log");
        """,
        "FileExists",
        "System.IO.File")]
    public async Task Reports_eventsource_logger_provider_writes_from_static_initializers(
        string staticMember,
        string predicate,
        string expectedApi)
    {
        var diagnostics = await AnalyzeAsync(Source(staticMember, predicate));

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(expectedApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string staticMember, string predicate)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Logging;

                [Plugin("eventsource-logging-host-api")]
                public sealed class EventSourceLoggingKernel : IEventKernel<string>
                {
            {{Indent(staticMember, 8)}}

                    public bool ShouldHandle(string e, HookContext context) => {{predicate}} && e.Length > 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

    private static string Indent(string text, int spaces)
        => string.Join(
            Environment.NewLine,
            text.Split('\n').Select(line => new string(' ', spaces) + line.TrimEnd()));

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var compilerErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilerErrors);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerEventSourceLoggingReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Concat(AspNetCoreReferencePackReferences())
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> AspNetCoreReferencePackReferences()
    {
        string[] fileNames =
        [
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "Microsoft.Extensions.Logging.dll",
            "Microsoft.Extensions.Logging.EventSource.dll",
            "Microsoft.Extensions.Options.dll"
        ];

        var trustedReferenceNames = new HashSet<string>(
            TrustedPlatformAssemblyPaths().Select(path => Path.GetFileName(path)!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in fileNames)
        {
            if (!trustedReferenceNames.Contains(fileName))
            {
                yield return MetadataReference.CreateFromFile(AspNetCoreTestReferences.FindAssembly(fileName));
            }
        }
    }

    private static IEnumerable<string> TrustedPlatformAssemblyPaths()
        => ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => TrustedPlatformAssemblyPaths().Select(reference => MetadataReference.CreateFromFile(reference));
}
