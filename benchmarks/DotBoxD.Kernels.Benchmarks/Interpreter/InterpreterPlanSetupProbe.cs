using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterPlanSetupProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 50_000;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var helperPlan = await PrepareAsync(host, HelperCallModuleJson(), policy);
        var directPlan = await PrepareAsync(host, DirectExpressionModuleJson(), policy);
        var twoAssignmentPlan = await PrepareAsync(host, TwoAssignmentModuleJson(), policy);
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        Console.WriteLine($"interpreter plan-setup executions = {Iterations:N0}");
        Console.WriteLine(
            "case                         total ms    allocated B       B/op   checksum   fuel/op  loops/op sandbox B/op host/op");
        RunCase(interpreter, helperPlan, options, "helper call, one iteration", input: 1, expectedValue: 3);
        RunCase(interpreter, helperPlan, options, "helper call, zero control", input: 0, expectedValue: 0);
        RunCase(interpreter, directPlan, options, "direct expression control", input: 1, expectedValue: 3);
        RunCase(interpreter, twoAssignmentPlan, options, "two-assignment control", input: 1, expectedValue: 6);
        RunCase(
            interpreter,
            directPlan,
            options,
            "direct expression, 20M loop",
            input: 20_000_000,
            expectedValue: DirectExpected(20_000_000),
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
        var watch = new Stopwatch();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("plan-setup probe unexpectedly left the synchronous interpreter path");
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

    private static string HelperCallModuleJson()
        => """
        {
          "id": "interpreter-plan-helper",
          "version": "1.0.0",
          "functions": [
            {
              "id": "increment",
              "visibility": "private",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [{
                "op": "return",
                "value": {
                  "op": "rem",
                  "left": { "op": "add", "left": { "var": "value" }, "right": { "i32": 3 } },
                  "right": { "i32": 1000003 }
                }
              }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "var": "iterations" },
                  "body": [{
                    "op": "set",
                    "name": "total",
                    "value": { "call": "increment", "args": [{ "var": "total" }] }
                  }]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string DirectExpressionModuleJson()
        => """
        {
          "id": "interpreter-plan-direct",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "total", "value": { "i32": 0 } },
              {
                "op": "forRange",
                "local": "i",
                "start": { "i32": 0 },
                "end": { "var": "iterations" },
                "body": [{
                  "op": "set",
                  "name": "total",
                  "value": {
                    "op": "rem",
                    "left": { "op": "add", "left": { "var": "total" }, "right": { "i32": 3 } },
                    "right": { "i32": 1000003 }
                  }
                }]
              },
              { "op": "return", "value": { "var": "total" } }
            ]
          }]
        }
        """;

    private static string TwoAssignmentModuleJson()
        => """
        {
          "id": "interpreter-plan-two-assignments",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "total", "value": { "i32": 0 } },
              { "op": "set", "name": "doubled", "value": { "i32": 0 } },
              {
                "op": "forRange",
                "local": "i",
                "start": { "i32": 0 },
                "end": { "var": "iterations" },
                "body": [
                  {
                    "op": "set",
                    "name": "total",
                    "value": {
                      "op": "rem",
                      "left": { "op": "add", "left": { "var": "total" }, "right": { "i32": 3 } },
                      "right": { "i32": 1000003 }
                    }
                  },
                  {
                    "op": "set",
                    "name": "doubled",
                    "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "total" } }
                  }
                ]
              },
              { "op": "return", "value": { "var": "doubled" } }
            ]
          }]
        }
        """;

    private static int DirectExpected(int iterations)
        => (int)((long)iterations * 3 % 1_000_003);

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
