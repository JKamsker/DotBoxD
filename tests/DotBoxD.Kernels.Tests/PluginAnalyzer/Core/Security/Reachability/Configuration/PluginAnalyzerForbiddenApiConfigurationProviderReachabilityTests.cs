using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiConfigurationProviderReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        """
        private static readonly IConfiguration Configuration =
            new ConfigurationBuilder().AddXmlFile("plugin.xml", optional: true, reloadOnChange: false).Build();
        """,
        "Microsoft.Extensions.Configuration.Xml")]
    [InlineData(
        """
        private static readonly bool FileExists = System.IO.File.Exists("plugin.txt");
        """,
        "System.IO.File")]
    public async Task Reports_forbidden_configuration_file_provider_static_initializers(
        string fieldInitializer,
        string expectedApi)
    {
        var source = Source(fieldInitializer);
        var diagnostics = await AnalyzeAsync(source);

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
            "DotBoxDPluginAnalyzerConfigurationProviderReachabilityTest",
            [syntaxTree],
            AnalyzerReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> AnalyzerReferences()
    {
        var references = TrustedPlatformReferences()
            .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location));

        return AddAspNetCoreReferencePackReferences(references);
    }

    private static IEnumerable<MetadataReference> AddAspNetCoreReferencePackReferences(
        IEnumerable<MetadataReference> references)
    {
        var existing = references
            .OfType<PortableExecutableReference>()
            .Select(reference => Path.GetFileNameWithoutExtension(reference.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            yield return reference;
        }

        foreach (var assemblyPath in AspNetCoreReferenceAssemblies())
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            if (existing.Add(assemblyName))
            {
                yield return MetadataReference.CreateFromFile(assemblyPath);
            }
        }
    }

    private static IEnumerable<string> AspNetCoreReferenceAssemblies()
    {
        var dotnetRoot = FindDotnetRoot();
        if (dotnetRoot is null)
        {
            yield break;
        }

        var packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.AspNetCore.App.Ref");

        if (!Directory.Exists(packRoot))
        {
            yield break;
        }

        var referenceDirectory = Directory.EnumerateDirectories(packRoot)
            .Select(path => Path.Combine(path, "ref", "net10.0"))
            .Where(Directory.Exists)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        if (referenceDirectory is null)
        {
            yield break;
        }

        foreach (var assemblyPath in Directory.EnumerateFiles(referenceDirectory, "*.dll"))
        {
            yield return assemblyPath;
        }
    }

    private static string? FindDotnetRoot()
    {
        for (var directory = new DirectoryInfo(typeof(object).Assembly.Location).Parent;
            directory is not null;
            directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "packs")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private static string Source(string fieldInitializer)
        => $$"""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Configuration;

                [Plugin("configuration-provider-host-api")]
                public sealed class ConfigurationProviderKernel : IEventKernel<string>
                {
            {{Indent(fieldInitializer, 8)}}

                    public bool ShouldHandle(string e, HookContext context) => e.Length > 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

    private static string Indent(string text, int spaces)
        => string.Join(
            Environment.NewLine,
            text.Split('\n').Select(line => new string(' ', spaces) + line.TrimEnd()));
}
