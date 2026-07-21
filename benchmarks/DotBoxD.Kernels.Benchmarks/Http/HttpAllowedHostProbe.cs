using System.Diagnostics;
using System.Reflection;
using DotBoxD.Hosting.Http;

namespace DotBoxD.Kernels.Benchmarks.Http;

internal static class HttpAllowedHostProbe
{
    private const int HostCount = 1_000;
    private const int Warmup = 100;
    private const int Iterations = 10_000;
    private static readonly Func<IReadOnlySet<string>, Uri, bool> ProductionMatch = CreateMatchDelegate();

    public static void Run()
    {
        var scenarios = CreateScenarios();
        foreach (var scenario in scenarios)
        {
            _ = Measure(Warmup, scenario, ProductionMatch);
        }

        var measurements = new Measurement[scenarios.Length + 1];
        for (var i = 0; i < scenarios.Length; i++)
        {
            measurements[i] = Measure(Iterations, scenarios[i], ProductionMatch);
        }

        var nonDefaultLast = scenarios[1];
        _ = Measure(Warmup, nonDefaultLast, MatchesAllowedAuthorityBySet, "direct set control");
        measurements[^1] = Measure(
            Iterations,
            nonDefaultLast,
            MatchesAllowedAuthorityBySet,
            "direct set control");

        Console.WriteLine($"hosts = {HostCount:N0}");
        Console.WriteLine($"iterations = {Iterations:N0}");
        foreach (var measurement in measurements)
        {
            Write(measurement);
        }
    }

    private static Measurement Measure(
        int iterations,
        Scenario scenario,
        Func<IReadOnlySet<string>, Uri, bool> match,
        string? name = null)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sw = Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var matches = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (match(scenario.AllowedHosts, scenario.Target))
            {
                matches++;
            }
        }

        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        sw.Stop();
        var expectedMatches = scenario.Expected ? iterations : 0;
        if (matches != expectedMatches)
        {
            throw new InvalidOperationException(
                $"{scenario.Name} expected {expectedMatches:N0} matches but observed {matches:N0}.");
        }

        return new Measurement(
            name ?? scenario.Name,
            iterations,
            sw.Elapsed.TotalMilliseconds,
            allocatedAfter - allocatedBefore,
            matches);
    }

    private static bool MatchesAllowedAuthorityBySet(IReadOnlySet<string> allowedHosts, Uri uri)
        => allowedHosts.Count > 0 && allowedHosts.Contains(NormalizedAuthority(uri));

    private static string NormalizedAuthority(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    private static Scenario[] CreateScenarios()
        =>
        [
            new(
                "ignore-case non-default hit first",
                CreateNumberedHosts(":8443", targetFirst: true),
                new Uri($"https://api-{HostCount - 1}.example.com:8443/config"),
                Expected: true),
            new(
                "ignore-case non-default hit last",
                CreateNumberedHosts(":8443"),
                new Uri($"https://api-{HostCount - 1}.example.com:8443/config"),
                Expected: true),
            new(
                "ignore-case non-default miss",
                CreateNumberedHosts(":8443"),
                new Uri("https://missing.example.com:8443/config"),
                Expected: false),
            new(
                "ordinal comparer case-folded hit",
                CreateOrdinalCaseControl(),
                new Uri($"https://api-{HostCount - 1}.example.com:8443/config"),
                Expected: true),
            new(
                "default-port host hit last",
                CreateNumberedHosts(string.Empty),
                new Uri($"https://api-{HostCount - 1}.example.com/config"),
                Expected: true),
            new(
                "explicit default-port hit last",
                CreateNumberedHosts(":443"),
                new Uri($"https://api-{HostCount - 1}.example.com/config"),
                Expected: true),
            new(
                "wrong-scheme default-port miss",
                CreateNumberedHosts(":80"),
                new Uri($"https://api-{HostCount - 1}.example.com/config"),
                Expected: false),
            new(
                "IPv6 explicit default-port hit",
                CreateIpv6Control(),
                new Uri("https://[2001:db8::1]/config"),
                Expected: true)
        ];

    private static HashSet<string> CreateNumberedHosts(string suffix, bool targetFirst = false)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (targetFirst)
        {
            hosts.Add($"api-{HostCount - 1}.example.com{suffix}");
        }

        var count = targetFirst ? HostCount - 1 : HostCount;
        for (var i = 0; i < count; i++)
        {
            hosts.Add($"api-{i}.example.com{suffix}");
        }

        return hosts;
    }

    private static HashSet<string> CreateOrdinalCaseControl()
    {
        var hosts = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < HostCount - 1; i++)
        {
            hosts.Add($"api-{i}.example.com:8443");
        }

        hosts.Add($"API-{HostCount - 1}.EXAMPLE.COM:8443");
        return hosts;
    }

    private static HashSet<string> CreateIpv6Control()
    {
        var hosts = CreateNumberedHosts(":443");
        hosts.Remove($"api-{HostCount - 1}.example.com:443");
        hosts.Add("[2001:db8::1]:443");
        return hosts;
    }

    private static Func<IReadOnlySet<string>, Uri, bool> CreateMatchDelegate()
    {
        var method = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.SafeHttpUriAudit",
            throwOnError: true)!
            .GetMethod(
                "MatchesAllowedAuthority",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(IReadOnlySet<string>), typeof(Uri)])!;
        return method.CreateDelegate<Func<IReadOnlySet<string>, Uri, bool>>();
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-39} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,10:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,10:N1} B/op " +
            $"{measurement.Matches,10:N0} matches");

    private sealed record Scenario(
        string Name,
        IReadOnlySet<string> AllowedHosts,
        Uri Target,
        bool Expected);

    private readonly record struct Measurement(
        string Name,
        int Iterations,
        double Milliseconds,
        long AllocatedBytes,
        int Matches)
    {
        public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}
