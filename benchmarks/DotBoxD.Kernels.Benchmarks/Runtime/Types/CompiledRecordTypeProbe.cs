using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Runtime.Types;

internal static class CompiledRecordTypeProbe
{
    private const int DirectWarmupIterations = 20_000;
    private const int DirectIterations = 1_000_000;
    private const int CompiledWarmupIterations = 2_000;
    private const int CompiledIterations = 50_000;
    private static readonly SandboxExecutionOptions CompiledOptions = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    public static async Task RunAsync()
    {
        _ = MeasureDirect(DirectWarmupIterations, arity: 1, cached: false);
        _ = MeasureDirect(DirectWarmupIterations, arity: 1, cached: true);
        _ = MeasureDirect(DirectWarmupIterations, arity: 2, cached: false);
        _ = MeasureDirect(DirectWarmupIterations, arity: 2, cached: true);
        _ = MeasureDirect(DirectWarmupIterations, arity: 3, cached: true);

        using var host = CreateHost();
        var plan = await PrepareIdentityPlanAsync(host);
        var input = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(7), SandboxValue.FromString("x")]);
        var expectedUsage = await WarmCompiledAsync(host, plan, input);

        var legacyOne = MeasureDirect(DirectIterations, arity: 1, cached: false);
        var cachedOne = MeasureDirect(DirectIterations, arity: 1, cached: true);
        var legacyTwo = MeasureDirect(DirectIterations, arity: 2, cached: false);
        var cachedTwo = MeasureDirect(DirectIterations, arity: 2, cached: true);
        var cachedThree = MeasureDirect(DirectIterations, arity: 3, cached: true);
        var compiled = MeasureCompiled(host, plan, input, expectedUsage, CompiledIterations);

        Console.WriteLine($"direct iterations = {DirectIterations:N0}");
        Console.WriteLine("case                              ms      allocated B      B/op checksum");
        Write("TypeRecord(I32) legacy", legacyOne, DirectIterations);
        Write("TypeRecordCached(I32)", cachedOne, DirectIterations);
        Write("TypeRecord(I32,String) legacy", legacyTwo, DirectIterations);
        Write("TypeRecordCached(I32,String)", cachedTwo, DirectIterations);
        Write("TypeRecordCached arity-three fallback", cachedThree, DirectIterations);
        Console.WriteLine($"compiled iterations = {CompiledIterations:N0}");
        Write("compiled Record<I32,String> identity", compiled, CompiledIterations);
        Console.WriteLine(
            $"resources F/L/A/E/S = {expectedUsage.FuelUsed:N0}/{expectedUsage.LoopIterations:N0}/" +
            $"{expectedUsage.AllocatedBytes:N0}/{expectedUsage.CollectionElements:N0}/" +
            $"{expectedUsage.StringBytes:N0}");
    }

    private static Measurement MeasureDirect(int iterations, int arity, bool cached)
    {
        ForceGc();

        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            SandboxType[] fieldTypes = arity switch
            {
                1 => new[] { SandboxType.I32 },
                2 => [SandboxType.I32, SandboxType.String],
                3 => [SandboxType.I32, SandboxType.String, SandboxType.Bool],
                _ => throw new InvalidOperationException("unsupported probe arity")
            };
            var type = cached
                ? CompiledRuntime.TypeRecordCached(fieldTypes)
                : CompiledRuntime.TypeRecord(fieldTypes);
            checksum += type.Arguments.Count;
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static async Task<SandboxResourceUsage> WarmCompiledAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input)
    {
        SandboxResourceUsage? expectedUsage = null;
        for (var i = 0; i < CompiledWarmupIterations; i++)
        {
            var result = await host.ExecuteAsync(plan, "main", input, CompiledOptions);
            ValidateCompiledResult(result, input, expectedUsage);
            expectedUsage ??= result.ResourceUsage;
        }

        return expectedUsage!;
    }

    private static Measurement MeasureCompiled(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxResourceUsage expectedUsage,
        int iterations)
    {
        ForceGc();

        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var pending = host.ExecuteAsync(plan, "main", input, CompiledOptions);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("compiled record probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            ValidateCompiledResult(result, input, expectedUsage);
            checksum += ((RecordValue)result.Value!).Fields.Count;
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static void ValidateCompiledResult(
        SandboxExecutionResult result,
        SandboxValue input,
        SandboxResourceUsage? expectedUsage)
    {
        if (!result.Succeeded ||
            result.ActualMode != ExecutionMode.Compiled ||
            !result.ExecutionDispatched ||
            !ReferenceEquals(result.Value, input) ||
            result.Error is not null ||
            result.AuditEvents.Count != 0 ||
            (expectedUsage is not null && result.ResourceUsage != expectedUsage))
        {
            throw new InvalidOperationException("compiled record identity invariant changed");
        }
    }

    private static SandboxHost CreateHost()
        => SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static async Task<ExecutionPlan> PrepareIdentityPlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync("""
        {
          "id": "compiled-record-type-probe",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{
              "name": "value",
              "type": { "name": "Record", "arguments": ["I32", "String"] }
            }],
            "returnType": { "name": "Record", "arguments": ["I32", "String"] },
            "body": [{ "op": "return", "value": { "var": "value" } }]
          }]
        }
        """);
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        return await host.PrepareAsync(module, policy);
    }

    private static void Write(string name, Measurement measurement, int iterations)
        => Console.WriteLine(
            $"{name,-33} {measurement.Milliseconds,8:N1} {measurement.AllocatedBytes,16:N0} " +
            $"{measurement.AllocatedBytes / (double)iterations,9:N1} {measurement.Checksum,8:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        long Checksum);
}
