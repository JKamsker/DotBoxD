using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SafeIR.PluginAnalyzer;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerTests
{
    [Fact]
    public async Task Reports_unsupported_live_setting_type()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                [LiveSetting]
                public object Anything { get; set; } = new();

                public bool ShouldHandle(string e, HookContext context) => true;

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP020");
    }

    [Fact]
    public async Task Reports_file_io_inside_event_kernel()
    {
        var diagnostics = await AnalyzeAsync("""
            using SafeIR.Plugins;

            [GamePlugin("bad")]
            public sealed class BadKernel : IEventKernel<string>
            {
                public bool ShouldHandle(string e, HookContext context)
                {
                    System.IO.File.WriteAllText("x.txt", "bad");
                    return true;
                }

                public void Handle(string e, HookContext context) { }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SGP001");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "SafeIrPluginAnalyzerTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(GamePluginAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SafeIrPluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
