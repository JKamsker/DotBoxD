using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Runtime.Bindings;

internal static class CompiledBindingArityThreeProbe
{
    private const string BindingId = "string.substringBudgeted";
    private const int Warmup = 20_000;
    private const int Iterations = 500_000;
    private const int CallsPerCompiledExecution = 100;
    private const int CompiledWarmup = 1_000;
    private const int CompiledIterations = 10_000;
    private static readonly SandboxExecutionOptions CompiledOptions = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    public static async Task RunAsync()
    {
        _ = MeasureArrayBacked(Warmup);
        _ = MeasureScalar(Warmup);

        var arrayBacked = MeasureArrayBacked(Iterations);
        var scalar = MeasureScalar(Iterations);
        Console.WriteLine("case                         iterations      ns/call       B/call    checksum");
        Write("array-backed CallBinding", arrayBacked);
        Write("scalar CallBinding3", scalar);
        CompiledBindingArityThreeFallbackProbe.Run();

        var compiled = await MeasureCompiledAsync().ConfigureAwait(false);
        Console.WriteLine(
            $"compiled substring loop      {CompiledIterations,10:N0} " +
            $"{compiled.NanosecondsPerCall,12:N1} {compiled.BytesPerCall,12:N1} " +
            $"{compiled.Checksum,11:N0}");
    }

    private static async Task<Measurement> MeasureCompiledAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "compiled-binding-arity-three-probe",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "String",
            "body": [
              { "op": "set", "name": "result", "value": { "string": "abc" } },
              {
                "op": "forRange",
                "local": "i",
                "start": { "i32": 0 },
                "end": { "i32": {{CallsPerCompiledExecution}} },
                "body": [{
                  "op": "set",
                  "name": "result",
                  "value": {
                    "call": "string.substringBudgeted",
                    "args": [{ "string": "abcdefgh" }, { "i32": 2 }, { "i32": 3 }]
                  }
                }]
              },
              { "op": "return", "value": { "var": "result" } }
            ]
          }]
        }
        """);
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var plan = await host.PrepareAsync(module, policy).ConfigureAwait(false);
        for (var i = 0; i < CompiledWarmup; i++)
        {
            ValidateCompiled(await host.ExecuteAsync(plan, "main", SandboxValue.Unit, CompiledOptions)
                .ConfigureAwait(false));
        }

        ForceGc();
        var checksum = 0L;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < CompiledIterations; i++)
        {
            var pending = host.ExecuteAsync(plan, "main", SandboxValue.Unit, CompiledOptions);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "Compiled arity-three probe unexpectedly became asynchronous.");
            }

            checksum += ValidateCompiled(pending.Result);
        }

        stopwatch.Stop();
        var calls = CompiledIterations * CallsPerCompiledExecution;
        return new Measurement(
            stopwatch.Elapsed.TotalMilliseconds * 1_000_000 / calls,
            (GC.GetAllocatedBytesForCurrentThread() - before) / (double)calls,
            checksum);
    }

    private static int ValidateCompiled(SandboxExecutionResult result)
    {
        if (result is not
            {
                Succeeded: true,
                ActualMode: ExecutionMode.Compiled,
                ExecutionDispatched: true,
                Error: null,
                Value: StringValue { Value: "cde" }
            })
        {
            throw new InvalidOperationException("Compiled arity-three result invariant changed.");
        }

        return ((StringValue)result.Value).Value.Length;
    }

    private static Measurement MeasureScalar(int iterations)
    {
        using var context = CreateContext();
        var text = SandboxValue.FromString("abcdefgh");
        var start = SandboxValue.FromInt32(2);
        var length = SandboxValue.FromInt32(3);
        var checksum = 0L;

        ForceGc();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            CompiledRuntime.ChargeValueArray(context, 3);
            var value = CompiledRuntime.CallBinding3(context, BindingId, text, start, length);
            checksum += ((StringValue)value).Value.Length;
        }

        stopwatch.Stop();
        return Measurement.Create(stopwatch, before, iterations, checksum);
    }

    private static Measurement MeasureArrayBacked(int iterations)
    {
        using var context = CreateContext();
        var text = SandboxValue.FromString("abcdefgh");
        var start = SandboxValue.FromInt32(2);
        var length = SandboxValue.FromInt32(3);
        var checksum = 0L;

        ForceGc();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var args = CompiledRuntime.CreateLiteralValueArray(3);
            args[0] = text;
            args[1] = start;
            args[2] = length;
            CompiledRuntime.ChargeValueArray(context, 3);
            var value = CompiledRuntime.CallBinding(context, BindingId, args);
            checksum += ((StringValue)value).Value.Length;
        }

        stopwatch.Stop();
        return Measurement.Create(stopwatch, before, iterations, checksum);
    }

    private static SandboxContext CreateContext()
    {
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().AddDefaultPureBindings().Build(),
            NoopAuditSink.Instance,
            CancellationToken.None);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {Iterations,10:N0} {measurement.NanosecondsPerCall,12:N1} " +
            $"{measurement.BytesPerCall,12:N1} {measurement.Checksum,11:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        double NanosecondsPerCall,
        double BytesPerCall,
        long Checksum)
    {
        public static Measurement Create(
            Stopwatch stopwatch,
            long allocatedBefore,
            int iterations,
            long checksum)
            => new(
                stopwatch.Elapsed.TotalMilliseconds * 1_000_000 / iterations,
                (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / (double)iterations,
                checksum);
    }
}
