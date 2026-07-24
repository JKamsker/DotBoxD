using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class ServiceGeneratorCollisionIncrementalityProbe
{
    private const int WarmupIterations = 12;
    private const int EditIterations = 200;
    private const int ColdIterations = 5;
    private static readonly int[] ServiceCounts = [10, 100, 500];

    public static void Run()
    {
        var manifest = new List<string>
        {
            "schema=DotBoxD.Services.SourceGenerator.CollisionIncrementality/v1",
            "roslyn=" + typeof(CSharpCompilation).Assembly.GetName().Version,
            "language=" + LanguageVersion.Preview,
            "warmups=" + WarmupIterations.ToString(CultureInfo.InvariantCulture),
            "edit_iterations=" + EditIterations.ToString(CultureInfo.InvariantCulture),
            "cold_iterations=" + ColdIterations.ToString(CultureInfo.InvariantCulture),
        };

        Console.WriteLine("service-generator collision incrementality probe");
        Console.WriteLine($"warmups={WarmupIterations}|edit_iterations={EditIterations}|cold_iterations={ColdIterations}");
        foreach (var serviceCount in ServiceCounts)
        {
            foreach (var definition in ServiceGeneratorCollisionSources.Cases)
            {
                var scenario = ServiceGeneratorCollisionScenario.Create(serviceCount, definition);
                manifest.Add(scenario.ManifestEntry());
                scenario.Warm(WarmupIterations);
                WriteMeasurement(serviceCount, definition.Name, scenario.Measure(EditIterations));
                WriteFingerprints(serviceCount, definition.Name, scenario.FirstFingerprint, scenario.SecondFingerprint);
            }

            var coldDefinition = ServiceGeneratorCollisionSources.Cases[0];
            var cold = ServiceGeneratorCollisionScenario.Create(serviceCount, coldDefinition);
            WriteMeasurement(serviceCount, "cold-fresh-driver", cold.MeasureCold(ColdIterations));
            WriteFingerprints(serviceCount, "cold-fresh-driver", cold.FirstFingerprint, cold.FirstFingerprint);
        }

        Console.WriteLine("manifest_sha256=" + GeneratorRunFingerprint.HashLines(
            "DotBoxD.Services.SourceGenerator.CollisionIncrementality.Manifest/v1",
            manifest));
    }

    public static void RunTrackedAudit()
    {
        const int serviceCount = 10;
        var entries = new List<string>();
        Console.WriteLine($"service-generator tracked audit|services={serviceCount}");
        foreach (var definition in ServiceGeneratorCollisionSources.Cases)
        {
            var scenario = ServiceGeneratorCollisionScenario.Create(serviceCount, definition, trackSteps: true);
            var enter = $"tracked|services={serviceCount}|case={definition.Name}|direction=enter|{scenario.CurrentTrackedReasons()}";
            Console.WriteLine(enter);
            entries.Add(enter);
            scenario.Apply(useFirst: true);
            var exit = $"tracked|services={serviceCount}|case={definition.Name}|direction=exit|{scenario.CurrentTrackedReasons()}";
            Console.WriteLine(exit);
            entries.Add(exit);
        }

        Console.WriteLine("tracked_manifest_sha256=" + GeneratorRunFingerprint.HashLines(
            "DotBoxD.Services.SourceGenerator.CollisionIncrementality.Tracked/v1",
            entries));
    }

    private static void WriteMeasurement(
        int serviceCount,
        string name,
        ServiceGeneratorProbeMeasurement measurement)
    {
        var milliseconds = measurement.Elapsed.TotalMilliseconds;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "measure|services={0}|case={1}|iterations={2}|total_ms={3:F3}|us_per_edit={4:F3}|allocated_bytes={5}|bytes_per_edit={6:F3}",
            serviceCount,
            name,
            measurement.Iterations,
            milliseconds,
            milliseconds * 1_000 / measurement.Iterations,
            measurement.AllocatedBytes,
            measurement.AllocatedBytes / (double)measurement.Iterations));
    }

    private static void WriteFingerprints(
        int serviceCount,
        string name,
        GeneratorRunFingerprint first,
        GeneratorRunFingerprint second) =>
        Console.WriteLine(
            $"fingerprint|services={serviceCount}|case={name}|" +
            $"sources={first.SourceCount}/{second.SourceCount}|source_hash={first.SourceHash}/{second.SourceHash}|" +
            $"diagnostics={first.DiagnosticCount}/{second.DiagnosticCount}|" +
            $"diagnostic_hash={first.DiagnosticHash}/{second.DiagnosticHash}");
}
