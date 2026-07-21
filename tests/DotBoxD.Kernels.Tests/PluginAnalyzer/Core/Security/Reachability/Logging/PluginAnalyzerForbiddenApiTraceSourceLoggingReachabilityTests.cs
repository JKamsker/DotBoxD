using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiTraceSourceLoggingReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "trace-source-logging",
        """
        using var factory = LoggerFactory.Create(builder => builder.AddTraceSource("plugin"));
        factory.CreateLogger("plugin").LogWarning("plugin event {Event}", e);
        return true;
        """,
        "TraceSource")]
    [InlineData(
        "file-control",
        """
        return System.IO.File.Exists(e);
        """,
        "System.IO.File")]
    public async Task Reports_host_output_reached_from_event_kernel(
        string pluginId,
        string shouldHandleBody,
        string expectedForbiddenApi)
    {
        var diagnostics = await AnalyzeAsync(Source(pluginId, shouldHandleBody));

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains(expectedForbiddenApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string pluginId, string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Logging;

                [Plugin("{{pluginId}}")]
                public sealed class TraceSourceLoggingKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
            {{Indent(shouldHandleBody, 12)}}
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

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerTraceSourceLoggingReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Concat(AspNetCoreLoggingReferences())
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> AspNetCoreLoggingReferences()
    {
        yield return MetadataReference.CreateFromFile(
            AspNetCoreTestReferences.FindAssembly("Microsoft.Extensions.DependencyInjection.Abstractions.dll"));
        yield return MetadataReference.CreateFromFile(
            AspNetCoreTestReferences.FindAssembly("Microsoft.Extensions.Logging.Abstractions.dll"));
        yield return MetadataReference.CreateFromFile(
            AspNetCoreTestReferences.FindAssembly("Microsoft.Extensions.Logging.dll"));
        yield return MetadataReference.CreateFromFile(
            AspNetCoreTestReferences.FindAssembly("Microsoft.Extensions.Logging.TraceSource.dll"));
        yield return MetadataReference.CreateFromFile(
            AspNetCoreTestReferences.FindAssembly("Microsoft.Extensions.Options.dll"));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private static string Indent(string text, int spaces)
        => string.Join(
            Environment.NewLine,
            text.Split('\n').Select(line => new string(' ', spaces) + line.TrimEnd()));
}
