using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSystemXmlFileReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "xml-reader",
        """
        using var reader = System.Xml.XmlReader.Create("plugin.xml");
        return reader.NodeType == System.Xml.XmlNodeType.None;
        """,
        "System.Xml.XmlReader")]
    [InlineData(
        "xml-writer",
        """
        using var writer = System.Xml.XmlWriter.Create("plugin.xml");
        writer.WriteStartDocument();
        return true;
        """,
        "System.Xml.XmlWriter")]
    [InlineData(
        "file-control",
        """
        return System.IO.File.ReadAllText("plugin.txt").Length > 0;
        """,
        "System.IO.File")]
    public async Task Reports_forbidden_path_taking_file_apis_in_event_kernel(
        string pluginId,
        string shouldHandleBody,
        string expectedApi)
    {
        var source = Source(pluginId, shouldHandleBody);
        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains(expectedApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "xml-reader-memory",
        """
        using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(e));
        return reader.NodeType == System.Xml.XmlNodeType.None;
        """,
        "System.Xml.XmlReader")]
    [InlineData(
        "xml-writer-memory",
        """
        using var writer = System.Xml.XmlWriter.Create(new System.Text.StringBuilder());
        writer.WriteStartDocument();
        return true;
        """,
        "System.Xml.XmlWriter")]
    public async Task Does_not_report_memory_factories_as_xml_file_io(
        string pluginId,
        string shouldHandleBody,
        string xmlApi)
    {
        var diagnostics = await AnalyzeAsync(Source(pluginId, shouldHandleBody));

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK001" &&
                diagnostic.GetMessage().Contains(xmlApi, StringComparison.Ordinal));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerSystemXmlFileReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
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

    private static string Source(string pluginId, string shouldHandleBody)
        => $$"""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("{{pluginId}}")]
                public sealed class SystemXmlFileKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
            {{Indent(shouldHandleBody, 12)}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

    private static string Indent(string text, int spaces)
        => string.Join(
            Environment.NewLine,
            text.Split('\n').Select(line => new string(' ', spaces) + line.TrimEnd()));
}
