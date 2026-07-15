using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterWhilePlanSetupProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 50_000;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder => builder.UseInterpreter());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var singlePlan = await PrepareAsync(host, SingleAssignmentModule, policy);
        var noWhilePlan = await PrepareAsync(host, NoWhileControlModule, policy);
        var twoAssignmentPlan = await PrepareAsync(host, TwoAssignmentModule, policy);
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        Console.WriteLine($"interpreter while plan-setup executions = {Iterations:N0}");
        Console.WriteLine(
            "case                         total ms    allocated B       B/op   checksum   fuel/op  loops/op sandbox B/op host/op");
        RunCase(interpreter, singlePlan, options, "one assignment, one loop", 1, 1);
        RunCase(interpreter, singlePlan, options, "one assignment, zero loop", 0, 0);
        RunCase(interpreter, noWhilePlan, options, "no-while zero control", 0, 0);
        RunCase(interpreter, twoAssignmentPlan, options, "two-assignment control", 1, 2);
        RunCase(
            interpreter,
            singlePlan,
            options,
            "one assignment, 20M loop",
            20_000_000,
            20_000_000,
            warmupIterations: 1,
            measurementIterations: 1);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy);
    }

    private static void RunCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        string name,
        int input,
        int expectedValue,
        int warmupIterations = WarmupIterations,
        int measurementIterations = Iterations)
    {
        var value = SandboxValue.FromInt32(input);
        _ = Measure(interpreter, plan, value, options, expectedValue, warmupIterations);
        ForceGc();
        var measurement = Measure(interpreter, plan, value, options, expectedValue, measurementIterations);
        var usage = measurement.Usage;
        Console.WriteLine(
            $"{name,-28} {measurement.ElapsedMilliseconds,8:N1} {measurement.Bytes,14:N0} " +
            $"{measurement.Bytes / (double)measurementIterations,10:N1} " +
            $"{measurement.Checksum,10:N0} {usage.FuelUsed,9:N0} {usage.LoopIterations,9:N0} " +
            $"{usage.AllocatedBytes,12:N0} {usage.HostCalls,7:N0}");
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        int expectedValue,
        int iterations)
    {
        long checksum = 0;
        ResourceUsageInvariant? expectedUsage = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("while plan-setup probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
            }

            var actual = ((I32Value)result.Value!).Value;
            if (actual != expectedValue)
            {
                throw new InvalidOperationException($"expected {expectedValue}, got {actual}");
            }

            checksum += actual;
            var usage = ResourceUsageInvariant.From(result.ResourceUsage);
            expectedUsage ??= usage;
            if (usage != expectedUsage.Value)
            {
                throw new InvalidOperationException("sandbox resource usage changed between identical executions");
            }
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage ?? default);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private const string SingleAssignmentModule = """
    {
      "id": "interpreter-while-single-assignment",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "limit", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "counter", "value": { "i32": 0 } },
          {
            "op": "while",
            "condition": { "op": "lt", "left": { "var": "counter" }, "right": { "var": "limit" } },
            "body": [{
              "op": "set",
              "name": "counter",
              "value": { "op": "add", "left": { "var": "counter" }, "right": { "i32": 1 } }
            }]
          },
          { "op": "return", "value": { "var": "counter" } }
        ]
      }]
    }
    """;

    private const string NoWhileControlModule = """
    {
      "id": "interpreter-while-no-loop-control",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "limit", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "counter", "value": { "i32": 0 } },
          { "op": "return", "value": { "var": "counter" } }
        ]
      }]
    }
    """;

    private const string TwoAssignmentModule = """
    {
      "id": "interpreter-while-two-assignments",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "limit", "type": "I32" }],
        "returnType": "I32",
        "body": [
          { "op": "set", "name": "counter", "value": { "i32": 0 } },
          { "op": "set", "name": "doubled", "value": { "i32": 0 } },
          {
            "op": "while",
            "condition": { "op": "lt", "left": { "var": "counter" }, "right": { "var": "limit" } },
            "body": [
              {
                "op": "set",
                "name": "counter",
                "value": { "op": "add", "left": { "var": "counter" }, "right": { "i32": 1 } }
              },
              {
                "op": "set",
                "name": "doubled",
                "value": { "op": "add", "left": { "var": "counter" }, "right": { "var": "counter" } }
              }
            ]
          },
          { "op": "return", "value": { "var": "doubled" } }
        ]
      }]
    }
    """;

    private readonly record struct ResourceUsageInvariant(
        long FuelUsed,
        long LoopIterations,
        long AllocatedBytes,
        int HostCalls)
    {
        public static ResourceUsageInvariant From(SandboxResourceUsage usage)
            => new(usage.FuelUsed, usage.LoopIterations, usage.AllocatedBytes, usage.HostCalls);
    }

    private readonly record struct Measurement(
        double ElapsedMilliseconds,
        long Bytes,
        long Checksum,
        ResourceUsageInvariant Usage);
}
