using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Benchmarks.PluginAnalyzer;

/// <summary>Measures retained-driver edits through the broad InvokeAsync semantic transform.</summary>
internal static class InvokeAsyncResolutionProbe
{
    private const int OrdinaryCallCount = 500;
    private const int CustomCallCount = 50;
    private const int DiagnosticCallCount = 100;
    private const int WarmupIterations = 30;
    private const int Iterations = 300;
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static void Run()
    {
        var scenarios = new[]
        {
            Scenario.Create("resolved ordinary", OrdinaryCallCount, InvokeAsyncResolutionProbeSources.ResolvedCalls),
            Scenario.Create("user InvokeAsync", OrdinaryCallCount, InvokeAsyncResolutionProbeSources.UserInvokeAsyncCalls),
            Scenario.Create("unresolved InvokeAsync", OrdinaryCallCount, InvokeAsyncResolutionProbeSources.UnresolvedCalls),
            Scenario.Create("custom ProbeAsync", CustomCallCount, InvokeAsyncResolutionProbeSources.CustomCalls),
            Scenario.Create(
                "diagnostic/fallback",
                DiagnosticCallCount,
                InvokeAsyncResolutionProbeSources.DiagnosticCalls,
                expectedDiagnosticCount: DiagnosticCallCount + 1),
        };

        foreach (var scenario in scenarios)
        {
            RunIterations(scenario, WarmupIterations);
            scenario.ValidateOutput();
        }

        Console.WriteLine($"InvokeAsync retained-driver edits = {Iterations:N0}");
        Console.WriteLine("case                       calls   source hashes               diagnostic hashes           total ms    us/edit      allocated B       B/edit");
        foreach (var scenario in scenarios)
        {
            Write(scenario, Measure(scenario));
        }
    }

    private static Measurement Measure(Scenario scenario)
    {
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        RunIterations(scenario, Iterations);
        watch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        scenario.ValidateOutput();
        return new Measurement(watch.Elapsed, allocatedBytes);
    }

    private static void RunIterations(Scenario scenario, int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            scenario.ApplyEdit((i & 1) == 0);
        }
    }

    private static void Write(Scenario scenario, Measurement measurement)
        => Console.WriteLine(
            $"{scenario.Name,-25} {scenario.CallCount,5:N0}   {scenario.SourceHashes,-25}   " +
            $"{scenario.DiagnosticHashes,-25} {measurement.Elapsed.TotalMilliseconds,8:N1} " +
            $"{measurement.Elapsed.TotalMilliseconds * 1_000 / Iterations,10:N1} " +
            $"{measurement.AllocatedBytes,16:N0} {measurement.AllocatedBytes / (double)Iterations,12:N1}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(static path => MetadataReference.CreateFromFile(path));

    private readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes);

    private sealed class Scenario
    {
        private readonly CSharpCompilation _firstCompilation;
        private readonly CSharpCompilation _secondCompilation;
        private readonly OutputSnapshot _expectedFirst;
        private readonly OutputSnapshot _expectedSecond;
        private OutputSnapshot _expected;
        private GeneratorDriver _driver;

        private Scenario(
            string name,
            int callCount,
            GeneratorDriver driver,
            CSharpCompilation firstCompilation,
            CSharpCompilation secondCompilation,
            OutputSnapshot expectedFirst,
            OutputSnapshot expectedSecond)
        {
            Name = name;
            CallCount = callCount;
            _driver = driver;
            _firstCompilation = firstCompilation;
            _secondCompilation = secondCompilation;
            _expectedFirst = expectedFirst;
            _expectedSecond = expectedSecond;
            _expected = expectedSecond;
        }

        public string Name { get; }

        public int CallCount { get; }

        public string SourceHashes => HashPair(_expectedFirst.SourceHash, _expectedSecond.SourceHash);

        public string DiagnosticHashes => HashPair(_expectedFirst.DiagnosticHash, _expectedSecond.DiagnosticHash);

        public static Scenario Create(
            string name,
            int callCount,
            Func<char, int, string> source,
            int expectedDiagnosticCount = 0)
        {
            var infrastructure = CSharpSyntaxTree.ParseText(
                InvokeAsyncResolutionProbeSources.Infrastructure,
                ParseOptions,
                "Infrastructure.cs");
            var firstWorkload = CSharpSyntaxTree.ParseText(source('a', callCount), ParseOptions, "Workload.cs");
            var secondWorkload = CSharpSyntaxTree.ParseText(source('b', callCount), ParseOptions, "Workload.cs");
            var references = TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(global::DotBoxD.Abstractions.PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(global::DotBoxD.Kernels.SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(global::DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location));
            var firstCompilation = CSharpCompilation.Create(
                "InvokeAsyncResolution_" + name.Replace(' ', '_').Replace('/', '_'),
                [infrastructure, firstWorkload],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var secondCompilation = firstCompilation.ReplaceSyntaxTree(firstWorkload, secondWorkload);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                [new DotBoxD.Plugins.Analyzer.Analysis.PluginPackageGenerator().AsSourceGenerator()],
                parseOptions: ParseOptions);
            driver = driver.RunGenerators(firstCompilation);
            var expectedFirst = OutputSnapshot.Create(driver.GetRunResult());
            driver = driver.RunGenerators(secondCompilation);
            var expectedSecond = OutputSnapshot.Create(driver.GetRunResult());
            if (!ExpectedShape(expectedFirst, expectedDiagnosticCount) ||
                !ExpectedShape(expectedSecond, expectedDiagnosticCount))
            {
                throw new InvalidOperationException(
                    $"{name} produced unexpected diagnostics or generated-source shape.");
            }

            var scenario = new Scenario(
                name,
                callCount,
                driver,
                firstCompilation,
                secondCompilation,
                expectedFirst,
                expectedSecond);
            scenario.ValidateOutput();
            return scenario;
        }

        public void ApplyEdit(bool useFirstCompilation)
        {
            _driver = _driver.RunGenerators(useFirstCompilation ? _firstCompilation : _secondCompilation);
            _expected = useFirstCompilation ? _expectedFirst : _expectedSecond;
        }

        public void ValidateOutput()
        {
            var actual = OutputSnapshot.Create(_driver.GetRunResult());
            if (actual != _expected)
            {
                throw new InvalidOperationException(Name + " generated output or diagnostics changed across the edit.");
            }
        }

        private static bool ExpectedShape(OutputSnapshot output, int diagnosticCount)
            => output.DiagnosticCount == diagnosticCount &&
               output.Source.Contains("InvokeAsync_", StringComparison.Ordinal);

        private static string HashPair(string first, string second)
            => first == second ? first[..12] : first[..12] + "/" + second[..12];
    }

    private sealed record OutputSnapshot(string Source, string Diagnostics, int DiagnosticCount)
    {
        public string SourceHash => Hash(Source);

        public string DiagnosticHash => Hash(Diagnostics);

        public static OutputSnapshot Create(GeneratorDriverRunResult result)
        {
            var sources = string.Join(
                "\n---\n",
                result.Results.SelectMany(static generator => generator.GeneratedSources)
                    .OrderBy(static source => source.HintName, StringComparer.Ordinal)
                    .Select(static source => source.HintName + "\n" + source.SourceText));
            var diagnostics = string.Join(
                "\n",
                result.Diagnostics.OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
                    .Select(static diagnostic => diagnostic.Id + "|" + diagnostic.GetMessage()));
            return new OutputSnapshot(sources, diagnostics, result.Diagnostics.Length);
        }

        private static string Hash(string value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
