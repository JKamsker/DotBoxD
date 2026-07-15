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
        "environment configuration provider",
        """
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        return configuration["DOTBOXD_PLUGIN_TEST_KEY"] is not null;
        """,
        "Microsoft.Extensions.Configuration")]
    [InlineData(
        "direct System.IO control",
        "return System.IO.File.Exists(e);",
        "System.IO.File")]
    public async Task Reports_host_configuration_providers_reached_from_event_kernel(
        string testCase,
        string shouldHandleBody,
        string expectedApi)
    {
        var diagnostics = await AnalyzeAsync(Source(shouldHandleBody));

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
                using Microsoft.Extensions.Configuration;

                [Plugin("host-configuration-leak")]
                public sealed class HostConfigurationKernel : IEventKernel<string>
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
            "DotBoxDPluginAnalyzerConfigurationProviderReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Concat(AspNetCoreConfigurationReferences())
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> AspNetCoreConfigurationReferences()
    {
        var runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sharedRoot = Directory.GetParent(runtimeDirectory)?.Parent?.FullName;
        Assert.NotNull(sharedRoot);

        var aspNetCoreDirectory = Directory
            .EnumerateDirectories(Path.Combine(sharedRoot, "Microsoft.AspNetCore.App"))
            .OrderByDescending(path => Version.Parse(Path.GetFileName(path)))
            .First();

        string[] assemblies =
        [
            "Microsoft.Extensions.Configuration.Abstractions.dll",
            "Microsoft.Extensions.Configuration.dll",
            "Microsoft.Extensions.Configuration.EnvironmentVariables.dll",
            "Microsoft.Extensions.Primitives.dll"
        ];

        return assemblies.Select(assembly => MetadataReference.CreateFromFile(Path.Combine(aspNetCoreDirectory, assembly)));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
