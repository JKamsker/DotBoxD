using System.Diagnostics;
using DotBoxD.Hosting.Http;
using DotBoxD.Hosting.Http.Internal;

namespace DotBoxD.Kernels.Benchmarks.Http;

internal static class HttpAllowedHostProbe
{
    private const int IndexedIterations = 500_000;
    private const int CompatibilityIterations = 10_000;
    private const int WarmupIterations = 1_000;

    public static void Run()
    {
        var indexedScenarios = CreateIndexedScenarios();
        foreach (var scenario in indexedScenarios)
        {
            _ = Measure(WarmupIterations, scenario);
        }

        var compatibility = CreateCompatibilityScenario();
        _ = MeasureCompatibility(WarmupIterations, compatibility);

        Console.WriteLine("path                                    hosts  iterations      ns/op       B/op    matches");
        foreach (var scenario in indexedScenarios)
        {
            Write(Measure(IndexedIterations, scenario));
        }

        Write(MeasureCompatibility(CompatibilityIterations, compatibility));
    }

    private static Measurement Measure(int iterations, IndexedScenario scenario)
    {
        PrepareMeasurement();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var matches = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (SafeHttpUriAudit.MatchesAllowedAuthority(scenario.AllowedAuthorities, scenario.Target))
            {
                matches++;
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        ValidateMatches(scenario.Name, scenario.Expected, iterations, matches);
        return new Measurement(scenario.Name, scenario.HostCount, iterations, elapsed, allocated, matches);
    }

    private static Measurement MeasureCompatibility(int iterations, CompatibilityScenario scenario)
    {
        PrepareMeasurement();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var matches = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (SafeHttpUriAudit.MatchesAllowedAuthority(scenario.AllowedAuthorities, scenario.Target))
            {
                matches++;
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        ValidateMatches(scenario.Name, expected: true, iterations, matches);
        return new Measurement(scenario.Name, scenario.AllowedAuthorities.Count, iterations, elapsed, allocated, matches);
    }

    private static IndexedScenario[] CreateIndexedScenarios()
    {
        var scenarios = new List<IndexedScenario>();
        foreach (var count in new[] { 1, 16, 1_000 })
        {
            var authorities = NumberedAuthorities(count, ":8443");
            var index = ReadProductionIndex(authorities);
            scenarios.Add(new(
                $"indexed non-default hit ({count:N0})",
                count,
                index,
                new Uri($"https://api-{count - 1}.example.com:8443/config"),
                Expected: true));
            scenarios.Add(new(
                $"indexed non-default miss ({count:N0})",
                count,
                index,
                new Uri("https://missing.example.com:8443/config"),
                Expected: false));
        }

        scenarios.Add(CreateDefaultScenario("indexed default host hit", string.Empty, expected: true));
        scenarios.Add(CreateDefaultScenario("indexed explicit :443 hit", ":443", expected: true));
        scenarios.Add(CreateDefaultScenario("indexed zero-padded :0443 hit", ":0443", expected: true));
        scenarios.Add(CreateDefaultScenario("indexed wrong-scheme :80 miss", ":80", expected: false));
        return scenarios.ToArray();
    }

    private static IndexedScenario CreateDefaultScenario(string name, string suffix, bool expected)
    {
        const int count = 1_000;
        return new(
            name,
            count,
            ReadProductionIndex(NumberedAuthorities(count, suffix)),
            new Uri($"https://api-{count - 1}.example.com/config"),
            expected);
    }

    private static CompatibilityScenario CreateCompatibilityScenario()
    {
        const int count = 1_000;
        var authorities = NumberedAuthorities(count, ":8443").ToHashSet(StringComparer.Ordinal);
        authorities.Remove($"api-{count - 1}.example.com:8443");
        authorities.Add($"API-{count - 1}.EXAMPLE.COM:8443");
        return new(
            "generic ordinal case-folded hit",
            authorities,
            new Uri($"https://api-{count - 1}.example.com:8443/config"));
    }

    private static SafeHttpAllowedAuthorityIndex ReadProductionIndex(IEnumerable<string> authorities)
    {
        var grant = new CapabilityGrant(
            "net.http.get",
            new Dictionary<string, string>
            {
                ["allowedHosts"] = string.Join(',', authorities),
                ["maxResponseBytes"] = "1024"
            });
        return SafeHttpGrantReader.Read(grant).AllowedAuthorities;
    }

    private static string[] NumberedAuthorities(int count, string suffix)
    {
        var authorities = new string[count];
        for (var i = 0; i < authorities.Length; i++)
        {
            authorities[i] = $"api-{i}.example.com{suffix}";
        }

        return authorities;
    }

    private static void PrepareMeasurement()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void ValidateMatches(string name, bool expected, int iterations, int matches)
    {
        var expectedMatches = expected ? iterations : 0;
        if (matches != expectedMatches)
        {
            throw new InvalidOperationException(
                $"{name} expected {expectedMatches:N0} matches but observed {matches:N0}.");
        }
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-39} {measurement.HostCount,6:N0} " +
            $"{measurement.Iterations,11:N0} {measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.BytesPerOperation,10:N1} {measurement.Matches,10:N0}");

    private sealed record IndexedScenario(
        string Name,
        int HostCount,
        SafeHttpAllowedAuthorityIndex AllowedAuthorities,
        Uri Target,
        bool Expected);

    private sealed record CompatibilityScenario(
        string Name,
        IReadOnlySet<string> AllowedAuthorities,
        Uri Target);

    private readonly record struct Measurement(
        string Name,
        int HostCount,
        int Iterations,
        TimeSpan Elapsed,
        long AllocatedBytes,
        int Matches)
    {
        public double NanosecondsPerOperation => Elapsed.TotalNanoseconds / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}
