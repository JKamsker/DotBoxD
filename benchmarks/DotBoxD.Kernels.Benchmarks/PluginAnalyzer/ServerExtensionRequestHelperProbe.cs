using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Kernels.Benchmarks.PluginAnalyzer;

/// <summary>
/// Measures cold generation of service-backed server-extension proxies with complex request parameters.
/// Source trees are parsed once, and measured runs alternate between semantically identical trivia snapshots.
/// </summary>
internal static class ServerExtensionRequestHelperProbe
{
    private const int WarmupIterations = 2;
    private const int Iterations = 10;
    private static readonly int[] ProxyCounts = [1, 10, 100];
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static void Run()
    {
        Console.WriteLine($"server-extension request-helper cold generations = {Iterations:N0}");
        Console.WriteLine("proxies  total ms   ms/run     allocated B       B/run  UTF-8 B    chars    lines  helpers  helpers/proxy");
        foreach (var proxyCount in ProxyCounts)
        {
            var scenario = Scenario.Create(proxyCount);
            scenario.WarmUp();
            var measurement = scenario.Measure();
            Write(proxyCount, measurement);
        }
    }

    private static void Write(int proxyCount, Measurement measurement)
        => Console.WriteLine(
            $"{proxyCount,7:N0} " +
            $"{measurement.Elapsed.TotalMilliseconds,9:N1} " +
            $"{measurement.Elapsed.TotalMilliseconds / Iterations,8:N2} " +
            $"{measurement.AllocatedBytes,15:N0} " +
            $"{measurement.AllocatedBytes / (double)Iterations,11:N0} " +
            $"{measurement.Output.Utf8Bytes,8:N0} " +
            $"{measurement.Output.Characters,8:N0} " +
            $"{measurement.Output.Lines,8:N0} " +
            $"{measurement.Output.HelperDeclarations,8:N0} " +
            $"{measurement.Output.HelpersPerProxy,14:N0}");

    private static GeneratorDriverRunResult Generate(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DotBoxD.Plugins.Analyzer.Analysis.PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        return driver.RunGenerators(compilation).GetRunResult();
    }

    private static string Source(int proxyCount, char editMarker)
    {
        var source = new StringBuilder();
        source.Append("// edit ").Append(editMarker).AppendLine();
        source.AppendLine("using System.Collections.Generic;");
        source.AppendLine("using System.Threading.Tasks;");
        source.AppendLine("using DotBoxD.Abstractions;");
        source.AppendLine("using DotBoxD.Plugins;");
        source.AppendLine("namespace RequestHelperProbe;");
        for (var i = 0; i < proxyCount; i++)
        {
            source.Append("public interface IRequestService").Append(i).AppendLine();
            source.AppendLine("{");
            source.AppendLine("    ValueTask<int> ExecuteAsync(List<int> values);");
            source.AppendLine("}");
            source.Append("[ServerExtension(\"request-helper-").Append(i)
                .Append("\", typeof(IRequestService").Append(i).AppendLine("))]");
            source.Append("public sealed partial class RequestKernel").Append(i).AppendLine();
            source.AppendLine("{");
            source.AppendLine("    public int Execute(List<int> values, HookContext context) => values.Count;");
            source.AppendLine("}");
        }

        return source.ToString();
    }

    private static IEnumerable<MetadataReference> References()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Plugins.PluginServer).Assembly.Location));

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct OutputMetrics(
        int Utf8Bytes,
        int Characters,
        int Lines,
        int HelperDeclarations,
        int HelpersPerProxy);

    private readonly record struct Measurement(
        TimeSpan Elapsed,
        long AllocatedBytes,
        OutputMetrics Output);

    private sealed class Scenario
    {
        private readonly int _proxyCount;
        private readonly CSharpCompilation _firstCompilation;
        private readonly CSharpCompilation _secondCompilation;
        private readonly string _expectedOutput;
        private readonly OutputMetrics _output;

        private Scenario(
            int proxyCount,
            CSharpCompilation firstCompilation,
            CSharpCompilation secondCompilation,
            string expectedOutput,
            OutputMetrics output)
        {
            _proxyCount = proxyCount;
            _firstCompilation = firstCompilation;
            _secondCompilation = secondCompilation;
            _expectedOutput = expectedOutput;
            _output = output;
        }

        public static Scenario Create(int proxyCount)
        {
            var firstTree = CSharpSyntaxTree.ParseText(Source(proxyCount, 'a'), ParseOptions, "RequestHelpers.cs");
            var secondTree = CSharpSyntaxTree.ParseText(Source(proxyCount, 'b'), ParseOptions, "RequestHelpers.cs");
            var firstCompilation = CSharpCompilation.Create(
                "ServerExtensionRequestHelpers",
                [firstTree],
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var secondCompilation = firstCompilation.ReplaceSyntaxTree(firstTree, secondTree);
            var firstResult = Generate(firstCompilation);
            var expectedOutput = Validate(firstResult, proxyCount);
            var output = Metrics(firstResult, proxyCount);
            var secondResult = Generate(secondCompilation);
            var secondOutput = Validate(secondResult, proxyCount);
            if (!string.Equals(expectedOutput, secondOutput, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Generated output changed across the balanced trivia edit.");
            }

            return new Scenario(proxyCount, firstCompilation, secondCompilation, expectedOutput, output);
        }

        public void WarmUp()
        {
            for (var i = 0; i < WarmupIterations; i++)
            {
                Validate(Generate(Compilation(i)), _proxyCount);
            }
        }

        public Measurement Measure()
        {
            ForceGc();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            GeneratorDriverRunResult? result = null;
            for (var i = 0; i < Iterations; i++)
            {
                result = Generate(Compilation(i));
            }

            watch.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var output = Validate(
                result ?? throw new InvalidOperationException("No measured generator run completed."),
                _proxyCount);
            if (!string.Equals(_expectedOutput, output, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Measured generated output did not match the validated baseline.");
            }

            return new Measurement(watch.Elapsed, allocated, _output);
        }

        private CSharpCompilation Compilation(int iteration)
            => (iteration & 1) == 0 ? _firstCompilation : _secondCompilation;

        private static string Validate(GeneratorDriverRunResult result, int proxyCount)
        {
            var errors = result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
            }

            var sources = result.Results.SelectMany(static generator => generator.GeneratedSources).ToArray();
            if (sources.Length != proxyCount)
            {
                throw new InvalidOperationException($"Expected {proxyCount} generated proxy sources, got {sources.Length}.");
            }

            return string.Join(
                "\n---\n",
                sources.OrderBy(static source => source.HintName, StringComparer.Ordinal)
                    .Select(static source => source.HintName + "\n" + source.SourceText));
        }

        private static OutputMetrics Metrics(GeneratorDriverRunResult result, int proxyCount)
        {
            var sources = result.Results.SelectMany(static generator => generator.GeneratedSources).ToArray();
            var helperCounts = sources.Select(static source => source.SyntaxTree.GetRoot()
                    .DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Count(static method => IsRequestHelper(method.Identifier.ValueText)))
                .ToArray();
            if (helperCounts.Any(count => count != helperCounts[0]))
            {
                throw new InvalidOperationException("Generated request-helper count differed between proxies.");
            }

            return new OutputMetrics(
                sources.Sum(static source => Encoding.UTF8.GetByteCount(source.SourceText.ToString())),
                sources.Sum(static source => source.SourceText.Length),
                sources.Sum(static source => source.SourceText.Lines.Count),
                helperCounts.Sum(),
                helperCounts[0]);
        }

        private static bool IsRequestHelper(string name)
            => name.StartsWith("WriteKernelRpcValue", StringComparison.Ordinal) ||
               string.Equals(name, "DateTimeToWireOffset", StringComparison.Ordinal);
    }
}
