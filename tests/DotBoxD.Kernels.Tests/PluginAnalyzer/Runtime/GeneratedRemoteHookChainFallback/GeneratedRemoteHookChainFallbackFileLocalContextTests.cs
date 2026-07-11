using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class GeneratedRemoteHookChainFallbackTests
{
    [Fact]
    public void Same_compilation_generated_server_file_local_context_reports_focused_hook_chain_diagnostic()
    {
        var compilation = CreateCompilation(GeneratedServerSource + """

            namespace ChainSample.Plugin
            {
            file sealed class FileAlphaPluginContext;

            [global::DotBoxD.Abstractions.GeneratePluginServer(Context = typeof(FileAlphaPluginContext))]
            public partial class FileAlphaPluginServer : ChainSample.Game.IAlphaWorld;

            public static class FileContextGeneratedFallbackUsage
            {
                public static void Configure(FileAlphaPluginServer server)
                    => server.Hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                        .Where(e => e.Distance <= 5)
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "file"));
            }
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult());
        var diagnostics = result.Diagnostics.ToArray();

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.GetMessage().Contains("Hook chain server context type", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("FileAlphaPluginContext", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("file-local", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            output.GetDiagnostics(),
            diagnostic => (diagnostic.Id == "CS0234" || diagnostic.Id == "CS0246") &&
                diagnostic.GetMessage().Contains("FileAlphaPluginContext", StringComparison.Ordinal));
        Assert.DoesNotContain(
            GeneratedSources(result),
            source => source.Contains("\"file\"", StringComparison.Ordinal));
    }
}
