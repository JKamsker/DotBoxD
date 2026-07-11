using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Benchmarks.PluginAnalyzer;

[MemoryDiagnoser]
public class PluginPackageGeneratorScaleBenchmarks
{
    private GeneratorDriver _driver = null!;
    private CSharpCompilation _compilation = null!;

    [Params(10, 100, 500)]
    public int KernelCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        _compilation = CSharpCompilation.Create(
            "GeneratorScale",
            [CSharpSyntaxTree.ParseText(Source(KernelCount), parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(global::DotBoxD.Abstractions.PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(global::DotBoxD.Kernels.SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        _driver = CSharpGeneratorDriver.Create(
            [new DotBoxD.Plugins.Analyzer.Analysis.PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
    }

    [Benchmark]
    public int RunGenerators()
        => _driver.RunGenerators(_compilation).GetRunResult().Diagnostics.Length;

    private static string Source(int count)
    {
        var source = new StringBuilder("using DotBoxD.Plugins;\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("[Plugin(\"scale.").Append(i).Append("\")] public sealed class Kernel")
                .Append(i).Append(" : IEventKernel<int> { public bool ShouldHandle(int e, HookContext c) => e >= ")
                .Append(i).Append("; public void Handle(int e, HookContext c) { } }\n");
        }

        return source.ToString();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));
}
