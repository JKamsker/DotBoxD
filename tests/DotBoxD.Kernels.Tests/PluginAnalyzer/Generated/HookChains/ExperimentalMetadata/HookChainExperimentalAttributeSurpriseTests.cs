using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class HookChainExperimentalAttributeSurpriseTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    [Fact]
    public void Remote_Run_preserves_or_rejects_experimental_event_types()
    {
        var result = CompileWithGenerator("""
            using System.Diagnostics.CodeAnalysis;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Regression.Game;

            [Experimental("DBXEXP_CHAIN_EVENT")]
            public sealed record DamageEvent(string TargetId, int Damage);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "damaged"));
            }
            """);

        AssertExperimentalContract(result, "DBXEXP_CHAIN_EVENT");
    }

    [Fact]
    public void Remote_RunLocal_preserves_or_rejects_experimental_projected_payload_types()
    {
        var result = CompileWithGenerator("""
            using System.Diagnostics.CodeAnalysis;
            using DotBoxD.Plugins.Runtime;

            namespace Regression.Game;

            public sealed record DamageEvent(string TargetId, int Damage);

            [Experimental("DBXEXP_CHAIN_PAYLOAD")]
            public sealed record DamagePayload(string TargetId, int Damage);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Select(e => new DamagePayload(e.TargetId, e.Damage))
                        .RunLocal((payload, ctx) => { _ = payload.TargetId; });
            }
            """);

        AssertExperimentalContract(result, "DBXEXP_CHAIN_PAYLOAD");
    }

    private static void AssertExperimentalContract(GeneratedCompilation result, string diagnosticId)
    {
        var dbxkDiagnostics = result.AllDiagnostics
            .Where(d => d.Id.StartsWith("DBXK", StringComparison.Ordinal))
            .ToArray();
        if (dbxkDiagnostics.Length > 0)
        {
            Assert.All(dbxkDiagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
            return;
        }

        var generatedExperimentalDiagnostics = result.AllDiagnostics
            .Where(d => d.Id == diagnosticId && IsGeneratedSource(d))
            .ToArray();
        var userExperimentalDiagnostics = result.AllDiagnostics
            .Where(d => d.Id == diagnosticId && !IsGeneratedSource(d))
            .ToArray();
        var generated = string.Join(Environment.NewLine, result.GeneratedSources);

        Assert.DoesNotContain(generatedExperimentalDiagnostics, _ => true);
        Assert.NotEmpty(userExperimentalDiagnostics);
        Assert.Contains(
            $"[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"{diagnosticId}\")]",
            generated,
            StringComparison.Ordinal);
    }

    private static GeneratedCompilation CompileWithGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainExperimentalMetadataTest",
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
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        return new GeneratedCompilation(
            generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToArray(),
            runResult.GeneratedTrees.Select(tree => tree.GetText().ToString()).ToArray());
    }

    private static bool IsGeneratedSource(Diagnostic diagnostic)
        => diagnostic.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private sealed record GeneratedCompilation(
        IReadOnlyList<Diagnostic> AllDiagnostics,
        IReadOnlyList<string> GeneratedSources);
}
