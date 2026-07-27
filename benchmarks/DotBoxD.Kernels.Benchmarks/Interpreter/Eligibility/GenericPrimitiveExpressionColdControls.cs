using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class GenericPrimitiveExpressionColdControls
{
    public static async Task RunAsync(
        SandboxHost host,
        SandboxPolicy policy,
        SandboxExecutionOptions options)
    {
        const int shallowDepth = 1;
        const int deepDepth = 96;
        var literal = await host.PrepareAsync(
            GenericPrimitiveExpressionProbeModules.F64LiteralComparison(shallowDepth), policy);
        var raw = await host.PrepareAsync(
            GenericPrimitiveExpressionProbeModules.F64Comparison(
                shallowDepth,
                leftDeep: true),
            policy);
        var rawDeep = await host.PrepareAsync(
            GenericPrimitiveExpressionProbeModules.F64Comparison(deepDepth, leftDeep: true),
            policy);
        var intrinsicDeep = await host.PrepareAsync(
            GenericPrimitiveExpressionProbeModules.F64IntrinsicComparison(
                deepDepth,
                leftDeep: true,
                arithmeticOnLeft: true),
            policy);
        var conversionDeep = await host.PrepareAsync(
            GenericPrimitiveExpressionProbeModules.F64ConversionComparison(
                deepDepth,
                leftDeep: true),
            policy);

        Console.WriteLine();
        Console.WriteLine(
            "cold admission (2,000 interpreters)       stage1 ns/B       stage2 ns/B       stage3 ns/B");
        WriteStages(
            "literal depth 1",
            literal,
            options,
            GenericPrimitiveExpressionProbeModules.ExpectedLiteralComparisonFuel(shallowDepth));
        WriteStages(
            "raw local depth 1",
            raw,
            options,
            GenericPrimitiveExpressionProbeModules.ExpectedComparisonFuel(shallowDepth));
        WriteStages(
            "raw local depth 96",
            rawDeep,
            options,
            GenericPrimitiveExpressionProbeModules.ExpectedComparisonFuel(deepDepth));
        WriteStages(
            "intrinsic depth 96",
            intrinsicDeep,
            options,
            GenericPrimitiveExpressionProbeModules.ExpectedIntrinsicComparisonFuel(deepDepth),
            hostCalls: 1);
        WriteStages(
            "conversion depth 96",
            conversionDeep,
            options,
            GenericPrimitiveExpressionProbeModules.ExpectedConversionComparisonFuel(deepDepth));
    }

    private static void WriteStages(
        string name,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        long fuel,
        int hostCalls = 0)
    {
        const int count = 2_000;
        var interpreters = Enumerable.Range(0, count)
            .Select(_ => new SandboxInterpreter())
            .ToArray();
        var nanoseconds = new double[3];
        var bytes = new double[3];
        for (var stage = 0; stage < bytes.Length; stage++)
        {
            ForceGc();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < interpreters.Length; i++)
            {
                var result = GenericPrimitiveExpressionMeasurement.Execute(
                    interpreters[i], plan, options);
                GenericPrimitiveExpressionMeasurement.RequireValue(result, true);
                if (result.ResourceUsage.FuelUsed != fuel ||
                    result.ResourceUsage.HostCalls != hostCalls)
                {
                    throw new InvalidOperationException(
                        $"cold admission metering changed: fuel={result.ResourceUsage.FuelUsed}, " +
                        $"hosts={result.ResourceUsage.HostCalls}");
                }
            }

            watch.Stop();
            nanoseconds[stage] = watch.Elapsed.TotalNanoseconds / count;
            bytes[stage] =
                (GC.GetAllocatedBytesForCurrentThread() - before) / (double)count;
        }

        Console.WriteLine(
            $"{name,-26} {nanoseconds[0],9:N1}/{bytes[0],-7:N1} " +
            $"{nanoseconds[1],9:N1}/{bytes[1],-7:N1} " +
            $"{nanoseconds[2],9:N1}/{bytes[2],-7:N1}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
