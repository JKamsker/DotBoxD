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
        var i32Type = SandboxType.I32;
        var stringType = SandboxType.String;
        _ = Measure(warmup, () => Kernels.Runtime.CompiledRuntime.TypeListCached(i32Type));
        _ = Measure(
            warmup,
            static () => Kernels.Runtime.CompiledRuntime.TypeList(
                Kernels.Runtime.CompiledRuntime.TypeList(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("I32"))));
        _ = Measure(
            warmup,
            static () => Kernels.Runtime.CompiledRuntime.TypeList(
                Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId")));
        _ = Measure(
            warmup,
            static () => Kernels.Runtime.CompiledRuntime.TypeList(
                Kernels.Runtime.CompiledRuntime.TypeRecord([SandboxType.I32])));
        _ = Measure(
            warmup,
            () => Kernels.Runtime.CompiledRuntime.TypeMapCached(stringType, i32Type));
        _ = Measure(
            warmup,
            static () => Kernels.Runtime.CompiledRuntime.TypeMap(
                Kernels.Runtime.CompiledRuntime.TypeScalar("String"),
                Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId")));
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
        var list = SandboxValue.FromList([i32], SandboxType.I32);
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue> { [SandboxValue.FromString("key")] = i32 },
            SandboxType.String,
            SandboxType.I32);
        var opaque = SandboxValue.FromOpaqueId("MonsterId", "monster-1");
        var opaqueType = SandboxType.Scalar("MonsterId");
        var opaqueList = SandboxValue.FromList([opaque], opaqueType);
        var opaqueMap = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue> { [SandboxValue.FromString("key")] = opaque },
            SandboxType.String,
            opaqueType);
        _ = MeasureReturn(
            warmup,
            () => Kernels.Runtime.CompiledRuntime.RequireValueType(
                list,
                Kernels.Runtime.CompiledRuntime.TypeListCached(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("I32"))),
            list);
        _ = MeasureReturn(
            warmup,
            () => Kernels.Runtime.CompiledRuntime.RequireValueType(
                map,
                Kernels.Runtime.CompiledRuntime.TypeMapCached(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("String"),
                    Kernels.Runtime.CompiledRuntime.TypeScalar("I32"))),
            map);
        _ = MeasureReturn(
            warmup,
            () => Kernels.Runtime.CompiledRuntime.RequireValueType(
                opaqueList,
                Kernels.Runtime.CompiledRuntime.TypeList(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId"))),
            opaqueList);
        _ = MeasureReturn(
            warmup,
            () => Kernels.Runtime.CompiledRuntime.RequireValueType(
                opaqueMap,
                Kernels.Runtime.CompiledRuntime.TypeMap(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("String"),
                    Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId"))),
            opaqueMap);

        var allocatedScalar = Measure(iterations, static () => SandboxType.Scalar("I32"));
        var runtimeBuiltIn = Measure(iterations, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("I32"));
        var runtimeGuid = Measure(iterations, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("Guid"));
        var runtimeOpaque = Measure(iterations, static () => Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId"));
        var runtimeList = Measure(
            iterations,
            () => Kernels.Runtime.CompiledRuntime.TypeListCached(i32Type));
        var runtimeNestedList = Measure(
            iterations,
            static () => Kernels.Runtime.CompiledRuntime.TypeList(
                Kernels.Runtime.CompiledRuntime.TypeList(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("I32"))));
        var runtimeOpaqueList = Measure(
            iterations,
            static () => Kernels.Runtime.CompiledRuntime.TypeList(
                Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId")));
        var runtimeRecordList = Measure(
            iterations,
            static () => Kernels.Runtime.CompiledRuntime.TypeList(
                Kernels.Runtime.CompiledRuntime.TypeRecord([SandboxType.I32])));
        var runtimeMap = Measure(
            iterations,
            () => Kernels.Runtime.CompiledRuntime.TypeMapCached(stringType, i32Type));
        var runtimeOpaqueMap = Measure(
            iterations,
            static () => Kernels.Runtime.CompiledRuntime.TypeMap(
                Kernels.Runtime.CompiledRuntime.TypeScalar("String"),
                Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId")));
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
        var runtimeListValidation = MeasureReturn(
            iterations,
            () => Kernels.Runtime.CompiledRuntime.RequireValueType(
                list,
                Kernels.Runtime.CompiledRuntime.TypeListCached(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("I32"))),
            list);
        var runtimeMapValidation = MeasureReturn(
            iterations,
            () => Kernels.Runtime.CompiledRuntime.RequireValueType(
                map,
                Kernels.Runtime.CompiledRuntime.TypeMapCached(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("String"),
                    Kernels.Runtime.CompiledRuntime.TypeScalar("I32"))),
            map);
        var runtimeOpaqueListValidation = MeasureReturn(
            iterations,
            () => Kernels.Runtime.CompiledRuntime.RequireValueType(
                opaqueList,
                Kernels.Runtime.CompiledRuntime.TypeList(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId"))),
            opaqueList);
        var runtimeOpaqueMapValidation = MeasureReturn(
            iterations,
            () => Kernels.Runtime.CompiledRuntime.RequireValueType(
                opaqueMap,
                Kernels.Runtime.CompiledRuntime.TypeMap(
                    Kernels.Runtime.CompiledRuntime.TypeScalar("String"),
                    Kernels.Runtime.CompiledRuntime.TypeScalar("MonsterId"))),
            opaqueMap);

        Console.WriteLine($"iterations = {iterations:N0}");
        Write("SandboxType.Scalar(\"I32\")", allocatedScalar);
        Write("CompiledRuntime.TypeScalar(\"I32\")", runtimeBuiltIn);
        Write("CompiledRuntime.TypeScalar(\"Guid\")", runtimeGuid);
        Write("CompiledRuntime.TypeScalar(\"MonsterId\")", runtimeOpaque);
        Write("CompiledRuntime.TypeListCached(I32)", runtimeList);
        Write("CompiledRuntime.TypeList(List<I32>)", runtimeNestedList);
        Write("CompiledRuntime.TypeList(MonsterId)", runtimeOpaqueList);
        Write("CompiledRuntime.TypeList(Record<I32>)", runtimeRecordList);
        Write("CompiledRuntime.TypeMapCached(String,I32)", runtimeMap);
        Write("CompiledRuntime.TypeMap(String,MonsterId)", runtimeOpaqueMap);
        Write("RequireType(I32, Scalar(\"I32\"))", genericValidation);
        Write("RequireType(I32, SandboxType.I32)", builtInValidation);
        Write("RequireType(Guid, TypeScalar(\"Guid\"))", runtimeGuidValidation);
        Write("RequireType(Guid, SandboxType.Guid)", builtInGuidValidation);
        Write("RequireValueType(List<I32>, TypeListCached(...))", runtimeListValidation);
        Write("RequireValueType(Map<String,I32>, TypeMapCached(...))", runtimeMapValidation);
        Write("RequireValueType(List<MonsterId>, TypeList(...))", runtimeOpaqueListValidation);
        Write("RequireValueType(Map<String,MonsterId>, TypeMap(...))", runtimeOpaqueMapValidation);
    }

    private static Measurement Measure(int iterations, Func<SandboxType> action)
        => Measure(iterations, () =>
        {
            var result = action();
            GC.KeepAlive(result);
        });

    private static Measurement MeasureReturn(
        int iterations,
        Func<SandboxValue> action,
        SandboxValue expected)
        => Measure(iterations, () =>
        {
            if (!ReferenceEquals(expected, action()))
            {
                throw new InvalidOperationException("Generated return validation changed the value instance.");
            }
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
