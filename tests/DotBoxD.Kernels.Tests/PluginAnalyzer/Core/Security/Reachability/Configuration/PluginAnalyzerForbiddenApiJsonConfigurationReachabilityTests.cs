using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiJsonConfigurationReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "JSON configuration provider",
        """
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile("plugin.json", optional: true, reloadOnChange: false)
            .Build();
        return configuration["secret"] is not null;
        """,
        "Microsoft.Extensions.Configuration.Json")]
    [InlineData(
        "System.IO control",
        "return System.IO.File.Exists(e);",
        "System.IO.File")]
    public async Task Reports_json_configuration_file_access_from_event_kernel(
        string testCase,
        string shouldHandleBody,
        string expectedForbiddenApi)
    {
        var source = Source(shouldHandleBody);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedForbiddenApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Configuration;

                [Plugin("json-configuration-host-file")]
                public sealed class JsonConfigurationKernel : IEventKernel<string>
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
            "DotBoxDPluginAnalyzerJsonConfigurationReachabilityTest",
            [syntaxTree],
            TestReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> TestReferences()
    {
        var references = TrustedPlatformReferences().ToList();
        var fileNames = new HashSet<string>(
            references.Select(reference => Path.GetFileName(reference.Display ?? string.Empty)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var assemblyName in RequiredAspNetCoreAssemblies())
        {
            if (fileNames.Add(assemblyName))
            {
                references.Add(MetadataReference.CreateFromFile(AspNetCoreReferencePath(assemblyName)));
            }
        }

        references.Add(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location));

        return references;
    }

    private static string[] RequiredAspNetCoreAssemblies()
        =>
        [
            "Microsoft.Extensions.Configuration.Abstractions.dll",
            "Microsoft.Extensions.Configuration.dll",
            "Microsoft.Extensions.Configuration.FileExtensions.dll",
            "Microsoft.Extensions.Configuration.Json.dll",
            "Microsoft.Extensions.FileProviders.Abstractions.dll",
            "Microsoft.Extensions.FileProviders.Physical.dll",
            "Microsoft.Extensions.Primitives.dll"
        ];

    private static string AspNetCoreReferencePath(string assemblyName)
    {
        var targetFramework = $"net{Environment.Version.Major}.0";
        var runtimeVersion = new DirectoryInfo(Path.GetDirectoryName(typeof(object).Assembly.Location)!).Name;
        var packRoot = Path.Combine(DotNetRoot(), "packs", "Microsoft.AspNetCore.App.Ref");
        var versionedCandidate = Path.Combine(packRoot, runtimeVersion, "ref", targetFramework, assemblyName);

        if (File.Exists(versionedCandidate))
        {
            return versionedCandidate;
        }

        var fallback = Directory
            .GetFiles(packRoot, assemblyName, SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}" +
                $"{targetFramework}{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .LastOrDefault();

        return fallback ?? throw new FileNotFoundException(
            $"Could not find {assemblyName} in the Microsoft.AspNetCore.App reference pack.");
    }

    private static string DotNetRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(object).Assembly.Location)!);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "packs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the dotnet packs directory.");
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
