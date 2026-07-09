using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

internal static class HookChainGeneratorTestSupport
{
    internal static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    internal static GeneratorDriverRunResult RunGenerator(string source)
        => RunGeneratorCore(source).Result;

    internal static GeneratorDriverRunResult RunGenerator(params SyntaxTree[] syntaxTrees)
        => RunGeneratorCore(syntaxTrees).Result;

    internal static Compilation RunGeneratorCompilation(string source)
        => RunGeneratorCore(source).Output;

    internal static (Compilation Output, GeneratorDriverRunResult Result) RunGeneratorCore(string source)
        => RunGeneratorCore([CSharpSyntaxTree.ParseText(source, ParseOptions)]);

    private static (Compilation Output, GeneratorDriverRunResult Result) RunGeneratorCore(SyntaxTree[] syntaxTrees)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDHookChainGeneratorTest",
            syntaxTrees,
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginServer).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(
                    typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (output, PluginGeneratorAssert.NoUnexpectedSourceGeneratorFailures(driver.GetRunResult()));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
