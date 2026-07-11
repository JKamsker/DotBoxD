using System.Text;
using BenchmarkDotNet.Attributes;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.SourceGenerator.EntryPoint;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class ServiceGeneratorScaleBenchmarks
{
    private GeneratorDriver _driver = null!;
    private CSharpCompilation _compilation = null!;

    [Params(10, 100, 500)]
    public int ContractCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        _compilation = CSharpCompilation.Create(
            "ServiceGeneratorScale",
            [CSharpSyntaxTree.ParseText(Source(ContractCount), parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        _driver = CSharpGeneratorDriver.Create(
            [new DotBoxDRpcGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
    }

    [Benchmark]
    public int RunGenerators()
        => _driver.RunGenerators(_compilation).GetRunResult().Diagnostics.Length;

    private static string Source(int count)
    {
        var source = new StringBuilder("using DotBoxD.Services.Attributes;\nusing System.Threading.Tasks;\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("public sealed record Request").Append(i).Append("(int Value, string Name);\n")
                .Append("[RpcService(\"scale.").Append(i).Append("\")] public interface IService")
                .Append(i).Append(" { ValueTask<int> InvokeAsync(Request").Append(i)
                .Append(" request); }\n");
        }

        return source.ToString();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));
}
