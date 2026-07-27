using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiKeyPerFileConfigurationReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "key-per-file-configuration",
        """
        private static readonly IConfiguration Configuration =
            new ConfigurationBuilder()
                .AddKeyPerFile("plugin-secrets", optional: true)
                .Build();
        """,
        "KeyPerFile")]
    [InlineData(
        "file-control",
        """
        private static readonly bool FileExists =
            System.IO.File.Exists("plugin-secrets");
        """,
        "System.IO.File")]
    public async Task Reports_forbidden_static_initializer_host_file_providers(
        string pluginId,
        string staticMember,
        string expectedApi)
    {
        var diagnostics = await AnalyzeAsync(Source(pluginId, staticMember));

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains(expectedApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerKeyPerFileConfigurationReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Concat(AspNetCoreConfigurationReferences())
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> AspNetCoreConfigurationReferences()
    {
        yield return MetadataReference.CreateFromFile(AspNetCoreAssembly("Microsoft.Extensions.Configuration.dll"));
        yield return MetadataReference.CreateFromFile(
            AspNetCoreAssembly("Microsoft.Extensions.Configuration.Abstractions.dll"));
        yield return MetadataReference.CreateFromFile(
            AspNetCoreAssembly("Microsoft.Extensions.Configuration.FileExtensions.dll"));
        yield return MetadataReference.CreateFromFile(
            AspNetCoreAssembly("Microsoft.Extensions.Configuration.KeyPerFile.dll"));
    }

    private static string AspNetCoreAssembly(string assemblyName)
    {
        var netCoreAppDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location) ??
            throw new InvalidOperationException("Unable to locate the .NET runtime directory.");
        var runtimeFamilyDirectory = Directory.GetParent(netCoreAppDirectory)?.FullName ??
            throw new InvalidOperationException("Unable to locate the runtime family directory.");
        var runtimeRoot = Directory.GetParent(runtimeFamilyDirectory)?.FullName ??
            throw new InvalidOperationException("Unable to locate the shared runtime root.");
        var aspNetCoreDirectory = Path.Combine(runtimeRoot, "Microsoft.AspNetCore.App");
        var version = Path.GetFileName(netCoreAppDirectory);
        var targetDirectory = Path.Combine(aspNetCoreDirectory, version);
        if (!Directory.Exists(targetDirectory))
        {
            targetDirectory = FindCompatibleAspNetCoreRuntimeDirectory(aspNetCoreDirectory, version);
        }

        return Path.Combine(targetDirectory, assemblyName);
    }

    private static string FindCompatibleAspNetCoreRuntimeDirectory(string aspNetCoreDirectory, string version)
    {
        if (!Version.TryParse(StripPrereleaseSuffix(version), out var runtimeVersion))
        {
            throw new InvalidOperationException($"Unable to parse the .NET runtime version '{version}'.");
        }

        if (!Directory.Exists(aspNetCoreDirectory))
        {
            throw new InvalidOperationException("Unable to locate the ASP.NET Core runtime directory.");
        }

        string? bestDirectory = null;
        Version? bestVersion = null;
        foreach (var directory in Directory.EnumerateDirectories(aspNetCoreDirectory))
        {
            var candidateName = Path.GetFileName(directory);
            if (Version.TryParse(StripPrereleaseSuffix(candidateName), out var candidateVersion) &&
                candidateVersion.Major == runtimeVersion.Major &&
                candidateVersion.Minor == runtimeVersion.Minor)
            {
                if (bestVersion is null || candidateVersion.CompareTo(bestVersion) > 0)
                {
                    bestDirectory = directory;
                    bestVersion = candidateVersion;
                }
            }
        }

        if (bestDirectory is not null)
        {
            return bestDirectory;
        }

        throw new InvalidOperationException(
            $"Unable to locate an ASP.NET Core runtime directory compatible with .NET runtime '{version}'.");
    }

    private static string StripPrereleaseSuffix(string version)
    {
        var suffixStart = version.IndexOf('-', StringComparison.Ordinal);
        return suffixStart < 0 ? version : version[..suffixStart];
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private static string Source(string pluginId, string staticMember)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Configuration;

                [Plugin("{{pluginId}}")]
                public sealed class KeyPerFileConfigurationKernel : IEventKernel<string>
                {
            {{Indent(staticMember, 8)}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

    private static string Indent(string text, int spaces)
        => string.Join(
            Environment.NewLine,
            text.Split('\n').Select(line => new string(' ', spaces) + line.TrimEnd()));
}
