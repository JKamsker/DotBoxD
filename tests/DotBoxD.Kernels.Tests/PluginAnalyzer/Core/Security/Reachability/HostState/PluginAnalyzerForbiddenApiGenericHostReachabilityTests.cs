using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiGenericHostReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "generic host startup",
        "Host.CreateDefaultBuilder().Build().Run();",
        "Microsoft.Extensions.Hosting")]
    [InlineData(
        "direct System.IO control",
        "System.IO.File.ReadAllText(\"/x\");",
        "System.IO.File")]
    public async Task Reports_generic_host_startup_reached_from_event_kernel(
        string testCase,
        string statement,
        string expectedForbiddenApi)
    {
        var source = Source(statement);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedForbiddenApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string Source(string statement)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Hosting;

                [Plugin("generic-host-startup")]
                public sealed class GenericHostKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{statement}}
                        return e.Length > 0;
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
            "DotBoxDPluginAnalyzerGenericHostReachabilityTest",
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

        foreach (var path in AspNetCoreReferenceAssemblyPaths())
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

    private static IEnumerable<string> AspNetCoreReferenceAssemblyPaths()
    {
        var packsRoot = Path.Combine(
            Path.GetPathRoot(Environment.SystemDirectory) ?? Path.DirectorySeparatorChar.ToString(),
            "usr",
            "share",
            "dotnet",
            "packs",
            "Microsoft.AspNetCore.App.Ref");
        if (!Directory.Exists(packsRoot))
        {
            yield break;
        }

        var refDirectory = Directory.EnumerateDirectories(packsRoot)
            .OrderByDescending(static path => path, StringComparer.Ordinal)
            .Select(path => Path.Combine(path, "ref", "net10.0"))
            .FirstOrDefault(Directory.Exists);
        if (refDirectory is null)
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(refDirectory, "Microsoft.Extensions.*.dll"))
        {
            yield return path;
        }
    }
}
