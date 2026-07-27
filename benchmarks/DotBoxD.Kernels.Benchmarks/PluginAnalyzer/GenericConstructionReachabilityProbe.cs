namespace DotBoxD.Kernels.Benchmarks.PluginAnalyzer;

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

internal static class GenericConstructionReachabilityProbe
{
    private static readonly int[] ForwardingCounts = [32, 64, 128, 256, 512];
    private const int Samples = 3;

    public static void Run()
    {
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());

        Console.WriteLine("Generic-construction reachability probe");
        Console.WriteLine($"samples = {Samples}; concurrent analyzer execution = false");
        Console.WriteLine("edges  declaration order          median ms     allocated B   diagnostics");
        foreach (var count in ForwardingCounts)
        {
            WriteMeasurement(
                count,
                "ascending",
                GenericConstructionReachabilityProbeSources.GenericChain(count, descending: false),
                analyzers,
                expectedForbiddenDiagnostics: 1);
            WriteMeasurement(
                count,
                "descending",
                GenericConstructionReachabilityProbeSources.GenericChain(count, descending: true),
                analyzers,
                expectedForbiddenDiagnostics: 1);
        }

        WriteMeasurement(
            ForwardingCounts[^1],
            "ordinary helper control",
            GenericConstructionReachabilityProbeSources.OrdinaryChain(ForwardingCounts[^1]),
            analyzers,
            expectedForbiddenDiagnostics: 1);
    }

    private static void WriteMeasurement(
        int count,
        string order,
        string source,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        int expectedForbiddenDiagnostics)
    {
        var compilation = GenericConstructionReachabilityProbeSources.CreateCompilation(source);
        RequireCleanCompilation(compilation);
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: null,
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);

        _ = Analyze(compilation, analyzers, options, expectedForbiddenDiagnostics);
        var samples = new Measurement[Samples];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            ForceGc();
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var watch = Stopwatch.StartNew();
            var diagnosticCount = Analyze(
                compilation,
                analyzers,
                options,
                expectedForbiddenDiagnostics);
            watch.Stop();
            samples[sample] = new Measurement(
                watch.Elapsed.TotalMilliseconds,
                GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
                diagnosticCount);
        }

        Array.Sort(samples, static (left, right) => left.ElapsedMilliseconds.CompareTo(right.ElapsedMilliseconds));
        var median = samples[samples.Length / 2];
        Console.WriteLine(
            $"{count,5}  {order,-25} {median.ElapsedMilliseconds,10:N2} " +
            $"{median.AllocatedBytes,15:N0} {median.DiagnosticCount,13}");
    }

    private static int Analyze(
        Compilation compilation,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        CompilationWithAnalyzersOptions options,
        int expectedForbiddenDiagnostics)
    {
        var diagnostics = compilation
            .WithAnalyzers(analyzers, options)
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
        if (diagnostics.Any(static diagnostic => diagnostic.Id == "AD0001"))
        {
            throw new InvalidOperationException("plugin analyzer failed during the probe");
        }

        var forbidden = diagnostics.Count(static diagnostic => diagnostic.Id == "DBXK001");
        if (forbidden != expectedForbiddenDiagnostics)
        {
            throw new InvalidOperationException(
                $"expected {expectedForbiddenDiagnostics} DBXK001 diagnostic(s), observed {forbidden}");
        }

        return diagnostics.Length;
    }

    private static void RequireCleanCompilation(Compilation compilation)
    {
        var error = compilation.GetDiagnostics().FirstOrDefault(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (error is not null)
        {
            throw new InvalidOperationException(error.ToString());
        }
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        double ElapsedMilliseconds,
        long AllocatedBytes,
        int DiagnosticCount);
}
