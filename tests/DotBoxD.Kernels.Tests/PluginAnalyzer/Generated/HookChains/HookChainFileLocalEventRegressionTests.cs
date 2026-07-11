using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChains;

public sealed class HookChainFileLocalEventRegressionTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    [Fact]
    public void Remote_hook_chain_rejects_file_local_event_types_before_interceptor_output()
    {
        var diagnostics = Diagnostics("""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Regression.Game;

            file sealed record FileDamageEvent(string TargetId, int Damage);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<FileDamageEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "damaged"));
            }
            """);

        var dbxDiagnostics = diagnostics.Where(d => d.Id == "DBXK100").ToArray();
        var compilerLeaks = diagnostics.Where(d => d.Id == "CS0234").ToArray();

        Assert.True(
            dbxDiagnostics.Length > 0 && compilerLeaks.Length == 0,
            "Expected a focused DBXK100 diagnostic and no raw CS0234 from generated hook-chain " +
            $"interceptors, but saw:{Environment.NewLine}{Format(diagnostics)}");
    }

    private static IReadOnlyList<Diagnostic> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainFileLocalEventTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Services.Peer.RpcPeer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(
                    typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);
        PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(generatorDiagnostics);

        return generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
    }

    private static string Format(IEnumerable<Diagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
