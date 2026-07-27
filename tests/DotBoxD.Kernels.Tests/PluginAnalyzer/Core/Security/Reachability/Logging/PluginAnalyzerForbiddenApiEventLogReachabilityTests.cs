using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEventLogReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_eventlog_logger_writes_from_event_kernel()
    {
        var source = Source("""
            using var factory = LoggerFactory.Create(builder => builder.AddEventLog());
            factory.CreateLogger("plugin").LogWarning("plugin event {Event}", e);
            return true;
            """);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("Microsoft.Extensions.Logging.EventLog", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_file_control_from_event_kernel()
    {
        var source = Source("""
            return System.IO.File.Exists(e);
            """);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Logging;

                [Plugin("event-log-write")]
                public sealed class EventLogKernel : IEventKernel<string>
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
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, "Source.cs");
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerEventLogReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Concat(AspNetCoreReferencePackReferences(
                    "Microsoft.Extensions.Logging.Abstractions.dll",
                    "Microsoft.Extensions.Logging.dll",
                    "Microsoft.Extensions.Logging.EventLog.dll",
                    "Microsoft.Extensions.Options.dll",
                    "Microsoft.Extensions.DependencyInjection.Abstractions.dll"))
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

    private static IEnumerable<MetadataReference> AspNetCoreReferencePackReferences(params string[] fileNames)
    {
        var referenceDirectory = FindAspNetCoreReferenceDirectory();
        var trustedPlatformAssemblyNames = new HashSet<string>(
            TrustedPlatformReferences().Select(reference => Path.GetFileName(reference.Display!)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in fileNames)
        {
            if (trustedPlatformAssemblyNames.Contains(fileName))
            {
                continue;
            }

            yield return MetadataReference.CreateFromFile(Path.Combine(referenceDirectory, fileName));
        }
    }

    private static string FindAspNetCoreReferenceDirectory()
    {
        var dotnetRoot = FindDotnetRoot();
        var packsRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.AspNetCore.App.Ref");
        var referenceDirectory = Directory.EnumerateDirectories(packsRoot)
            .Select(static directory => new
            {
                Directory = Path.Combine(directory, "ref", "net10.0"),
                Version = Version.TryParse(Path.GetFileName(directory), out var version) ? version : null,
            })
            .Where(static candidate => candidate.Version is { Major: 10 } && Directory.Exists(candidate.Directory))
            .OrderByDescending(static candidate => candidate.Version)
            .Select(static candidate => candidate.Directory)
            .FirstOrDefault();

        return referenceDirectory ?? throw new DirectoryNotFoundException(
            $"Could not find the ASP.NET Core .NET 10 reference pack under '{packsRoot}'.");
    }

    private static string FindDotnetRoot()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root) && HasAspNetCoreReferencePack(root))
        {
            return root;
        }

        var runtimeDirectory = Directory.GetParent(typeof(object).Assembly.Location);
        var appDirectory = runtimeDirectory?.Parent;
        var sharedDirectory = appDirectory?.Parent;
        var dotnetDirectory = sharedDirectory?.Parent;
        if (dotnetDirectory is not null && HasAspNetCoreReferencePack(dotnetDirectory.FullName))
        {
            return dotnetDirectory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the dotnet installation root.");
    }

    private static bool HasAspNetCoreReferencePack(string dotnetRoot)
    {
        var packsRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.AspNetCore.App.Ref");
        return Directory.Exists(packsRoot) &&
            Directory.EnumerateDirectories(packsRoot).Any(static directory =>
                Version.TryParse(Path.GetFileName(directory), out var version) &&
                version.Major == 10 &&
                Directory.Exists(Path.Combine(directory, "ref", "net10.0")));
    }
}
