using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class HookChainCodeRequirementAttributeSurpriseTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    [Theory]
    [InlineData("RequiresUnreferencedCode", "Trimming the marked event requires reflection.")]
    [InlineData("RequiresDynamicCode", "The marked event requires dynamic code.")]
    public void Remote_Run_preserves_code_requirement_metadata_on_generated_surfaces(
        string attributeName,
        string message)
    {
        var result = CompileWithGenerator($$"""
            using System.Diagnostics.CodeAnalysis;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Regression.Game;

            [{{attributeName}}("{{message}}")]
            public sealed record MarkedDamageEvent(string TargetId, int Damage);

            public sealed record PortableDamageEvent(string TargetId, int Damage);

            public static class Usage
            {
                public static void ConfigureMarked(RemoteHookRegistry hooks)
                    => hooks.On<MarkedDamageEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "marked"));

                public static void ConfigurePortable(RemoteHookRegistry hooks)
                    => hooks.On<PortableDamageEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "portable"));
            }
            """);

        Assert.DoesNotContain(result.AllDiagnostics, diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal));

        var generated = string.Join(Environment.NewLine, result.GeneratedSources);
        var emittedAttribute =
            $"[global::System.Diagnostics.CodeAnalysis.{attributeName}Attribute(\"{message}\")]";

        Assert.Equal(2, CountOccurrences(generated, emittedAttribute));
    }

    private static GeneratedCompilation CompileWithGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainCodeRequirementMetadataTest",
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

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

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
