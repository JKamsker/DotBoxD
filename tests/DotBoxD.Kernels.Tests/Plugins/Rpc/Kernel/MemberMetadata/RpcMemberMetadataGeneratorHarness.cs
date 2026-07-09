using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.MemberMetadata;

internal sealed record RpcMemberMetadataGeneratorResult(
    IReadOnlyList<string> GeneratedSources,
    Compilation OutputCompilation,
    IReadOnlySet<SyntaxTree> GeneratedTrees,
    IReadOnlyList<Diagnostic> GeneratorDiagnostics);

internal static class RpcMemberMetadataGeneratorHarness
{
    public static CSharpParseOptions ParseOptions { get; } =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static RpcMemberMetadataGeneratorResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDGeneratedPackageMemberMetadataTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var generatedTrees = driver.GetRunResult().GeneratedTrees.ToHashSet();
        var generatedSources = generatedTrees
            .Select(tree => tree.GetText().ToString())
            .ToArray();
        return new RpcMemberMetadataGeneratorResult(
            generatedSources,
            outputCompilation,
            generatedTrees,
            generatorDiagnostics);
    }

    public static IEnumerable<MetadataReference> References()
    {
        foreach (var reference in TrustedPlatformReferences())
        {
            yield return reference;
        }

        yield return MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location);
    }

    public static bool IsGeneratedDiagnostic(
        Diagnostic diagnostic,
        IReadOnlySet<SyntaxTree> generatedTrees)
        => diagnostic.Location.SourceTree is { } tree && generatedTrees.Contains(tree);

    public static void AssertGeneratedSourceContains(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string expectedSource)
        => Assert.Contains(
            generatedSources,
            source => source.Contains(generatedTypeName, StringComparison.Ordinal) &&
                      source.Contains(expectedSource, StringComparison.Ordinal));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
