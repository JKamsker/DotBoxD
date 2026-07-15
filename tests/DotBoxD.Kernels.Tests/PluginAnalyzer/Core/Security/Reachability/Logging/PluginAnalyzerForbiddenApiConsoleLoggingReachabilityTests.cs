using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiConsoleLoggingReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_console_logger_provider_writes_from_event_kernel()
    {
        var source = Source("""
            using var factory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                Microsoft.Extensions.Logging.ConsoleLoggerExtensions.AddConsole(builder);
            });
            var logger = factory.CreateLogger("plugin");
            logger.Log<string>(
                Microsoft.Extensions.Logging.LogLevel.Information,
                default,
                "message",
                null,
                static (state, _) => state);
            """);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("Microsoft.Extensions.Logging.Console", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_file_access_control_from_event_kernel()
    {
        var diagnostics = await AnalyzeAsync(Source("_ = System.IO.File.Exists(e);"));

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string statement)
        => $$"""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Logging;

                [Plugin("console-logging-host-api")]
                public sealed class ConsoleLoggingKernel : IEventKernel<string>
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
        var compilerErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilerErrors);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerConsoleLoggingReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Concat(AspNetCoreReferences())
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

    private static IEnumerable<MetadataReference> AspNetCoreReferences()
    {
        var coreDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("Unable to locate the .NET shared framework.");
        var dotnetRoot = Directory.GetParent(coreDirectory)?.Parent?.FullName
            ?? throw new InvalidOperationException("Unable to locate the .NET root.");
        var aspNetCoreDirectory = Path.Combine(dotnetRoot, "Microsoft.AspNetCore.App");
        var currentMajorVersion = Environment.Version.Major + ".";
        var sharedFrameworkDirectory = Directory.EnumerateDirectories(aspNetCoreDirectory, currentMajorVersion + "*")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .First();

        return
        [
            MetadataReference.CreateFromFile(Path.Combine(
                sharedFrameworkDirectory,
                "Microsoft.Extensions.DependencyInjection.Abstractions.dll")),
            MetadataReference.CreateFromFile(Path.Combine(
                sharedFrameworkDirectory,
                "Microsoft.Extensions.Logging.Abstractions.dll")),
            MetadataReference.CreateFromFile(Path.Combine(
                sharedFrameworkDirectory,
                "Microsoft.Extensions.Logging.dll")),
            MetadataReference.CreateFromFile(Path.Combine(
                sharedFrameworkDirectory,
                "Microsoft.Extensions.Logging.Console.dll")),
            MetadataReference.CreateFromFile(Path.Combine(
                sharedFrameworkDirectory,
                "Microsoft.Extensions.Options.dll")),
        ];
    }
}
