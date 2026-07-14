using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;

internal static class RuntimeTypeProbe
{
    public static void Run()
    {
        const int iterations = 2_000_000;
        const int warmup = 100_000;

        _ = Measure(warmup, static () => SandboxType.Scalar("I32"));
        _ = Measure(warmup, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("I32"));
        _ = Measure(warmup, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("Guid"));
        _ = Measure(warmup, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId"));
        var i32 = SandboxValue.FromInt32(42);
        var genericI32Type = SandboxType.Scalar("I32");
        _ = Measure(warmup, () => SandboxValueValidator.RequireType(i32, genericI32Type, "probe"));
        _ = Measure(warmup, () => SandboxValueValidator.RequireType(i32, SandboxType.I32, "probe"));
        var guid = SandboxValue.FromGuid(Guid.ParseExact("00112233-4455-6677-8899-aabbccddeeff", "D"));
        _ = Measure(
            warmup,
            () => SandboxValueValidator.RequireType(
                guid,
                Kernels.Runtime.CompiledRuntime.TypeScalar("Guid"),
                "probe"));
        _ = Measure(warmup, () => SandboxValueValidator.RequireType(guid, SandboxType.Guid, "probe"));

        var allocatedScalar = Measure(iterations, static () => SandboxType.Scalar("I32"));
        var runtimeBuiltIn = Measure(iterations, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("I32"));
        var runtimeGuid = Measure(iterations, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("Guid"));
        var runtimeOpaque = Measure(iterations, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId"));
        var genericValidation = Measure(
            iterations,
            () => SandboxValueValidator.RequireType(i32, genericI32Type, "probe"));
        var builtInValidation = Measure(
            iterations,
            () => SandboxValueValidator.RequireType(i32, SandboxType.I32, "probe"));
        var runtimeGuidValidation = Measure(
            iterations,
            () => SandboxValueValidator.RequireType(
                guid,
                Kernels.Runtime.CompiledRuntime.TypeScalar("Guid"),
                "probe"));
        var builtInGuidValidation = Measure(
            iterations,
            () => SandboxValueValidator.RequireType(guid, SandboxType.Guid, "probe"));

        Console.WriteLine($"iterations = {iterations:N0}");
        Write("SandboxType.Scalar(\"I32\")", allocatedScalar);
        Write("CompiledRuntime.TypeScalar(\"I32\")", runtimeBuiltIn);
        Write("CompiledRuntime.TypeScalar(\"Guid\")", runtimeGuid);
        Write("CompiledRuntime.TypeScalar(\"MonsterId\")", runtimeOpaque);
        Write("RequireType(I32, Scalar(\"I32\"))", genericValidation);
        Write("RequireType(I32, SandboxType.I32)", builtInValidation);
        Write("RequireType(Guid, TypeScalar(\"Guid\"))", runtimeGuidValidation);
        Write("RequireType(Guid, SandboxType.Guid)", builtInGuidValidation);
    }

    private static Measurement Measure(int iterations, Func<SandboxType> action)
        => Measure(iterations, () =>
        {
            var result = action();
            GC.KeepAlive(result);
        });

    private static Measurement Measure(int iterations, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            action();
        }

        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Measurement(sw.Elapsed.TotalMilliseconds, allocated);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine($"{name,-40} {measurement.Milliseconds,8:N1} ms {measurement.AllocatedBytes,14:N0} B");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes);
}
