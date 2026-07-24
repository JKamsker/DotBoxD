using System.Diagnostics;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Validation;

internal static class SandboxTypeValidationProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var declaredOpaqueIds = new HashSet<string>(StringComparer.Ordinal) { "PlayerId" };
        var noOpaqueIds = new HashSet<string>(StringComparer.Ordinal);
        var nestedDeclared = CreateType(SandboxType.Scalar("PlayerId"));
        var nestedBuiltIn = CreateType(SandboxType.String);
        var forbidden = SandboxType.Record([
            SandboxType.I32,
            SandboxType.List(SandboxType.Scalar("System.String"))
        ]);
        var unknownComposite = new SandboxType("Tuple", [SandboxType.I32, SandboxType.String]);
        var atDepthLimit = WrapInLists(SandboxType.I32, count: 8);
        var beyondDepthLimit = WrapInLists(atDepthLimit, count: 1);

        var lanes = new[] {
            new Lane(
                "legacy known+forbidden",
                () => nestedDeclared.IsKnown(declaredOpaqueIds) && !nestedDeclared.IsForbidden(),
                Expected: true),
            new Lane("declared known only", () => nestedDeclared.IsKnown(declaredOpaqueIds), Expected: true),
            new Lane("open known only", () => nestedDeclared.IsKnown(), Expected: true),
            new Lane("undeclared opaque", () => nestedDeclared.IsKnown(noOpaqueIds), Expected: false),
            new Lane("built-in known only", () => nestedBuiltIn.IsKnownBuiltIn(), Expected: true),
            new Lane("forbidden walk only", () => nestedDeclared.IsForbidden(), Expected: false),
            new Lane("forbidden rejected", () => forbidden.IsKnown(), Expected: false),
            new Lane("unknown composite", () => unknownComposite.IsKnown(), Expected: false),
            new Lane("depth limit known", () => atDepthLimit.IsKnownBuiltIn(), Expected: true),
            new Lane("over-depth rejected", () => beyondDepthLimit.IsKnownBuiltIn(), Expected: false)
        };

        foreach (var lane in lanes)
        {
            _ = Measure(lane, Warmup);
        }

        Console.WriteLine($"sandbox type validation, iterations = {Iterations:N0}");
        Console.WriteLine("case                      result       ms    ns/op    allocated B   checksum");
        foreach (var lane in lanes)
        {
            Print(lane, Measure(lane, Iterations));
        }
    }

    private static SandboxType CreateType(SandboxType mapKey)
    {
        var inventoryEntry = SandboxType.Record([
            SandboxType.I32,
            SandboxType.String,
            SandboxType.Map(SandboxType.String, SandboxType.List(SandboxType.I64))
        ]);

        return SandboxType.Record([
            SandboxType.Map(mapKey, SandboxType.List(inventoryEntry)),
            SandboxType.List(SandboxType.Map(SandboxType.String, SandboxType.Record([
                SandboxType.I64,
                SandboxType.F64,
                mapKey
            ]))),
            mapKey
        ]);
    }

    private static SandboxType WrapInLists(SandboxType type, int count)
    {
        for (var i = 0; i < count; i++)
        {
            type = SandboxType.List(type);
        }

        return type;
    }

    private static Measurement Measure(Lane lane, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            if (lane.Validate() == lane.Expected)
            {
                checksum++;
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        return Measurement.Create(
            elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static void Print(Lane lane, Measurement measurement)
        => Console.WriteLine(
            $"{lane.Name,-25} {lane.Expected,5} {measurement.Milliseconds,8:N1} " +
            $"{measurement.NanosecondsPerOperation,8:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{measurement.Checksum,10:N0}");

    private sealed record Lane(string Name, Func<bool> Validate, bool Expected);

    private readonly record struct Measurement(
        double Milliseconds,
        double NanosecondsPerOperation,
        long AllocatedBytes,
        int Checksum)
    {
        public static Measurement Create(
            double milliseconds,
            long allocatedBytes,
            int checksum,
            int iterations)
        {
            if (checksum != iterations)
            {
                throw new InvalidOperationException(
                    $"sandbox type validation result changed: {checksum:N0}/{iterations:N0} checks matched");
            }

            return new(
                milliseconds,
                milliseconds * 1_000_000 / iterations,
                allocatedBytes,
                checksum);
        }
    }
}
