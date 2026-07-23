using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class I64SingleAssignmentLoopAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task I64_loop_planning_avoids_slot_predicate_allocations()
    {
        using var host = SandboxTestHost.Create();
        var singlePlan = await PrepareAsync(host, SingleAssignmentModule);
        var twoPlan = await PrepareAsync(host, TwoAssignmentModule);
        var sourceOrderedPlan = await PrepareAsync(host, SourceOrderedModule);
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        var single = MeasureCase(interpreter, singlePlan, options, input: 1, expectedValue: 4);
        var zero = MeasureCase(interpreter, singlePlan, options, input: 0, expectedValue: 1);
        var two = MeasureCase(interpreter, twoPlan, options, input: 1, expectedValue: 8);
        var twoZero = MeasureCase(interpreter, twoPlan, options, input: 0, expectedValue: 0);
        var sourceOrdered = MeasureCase(interpreter, sourceOrderedPlan, options, input: 1, expectedValue: 8);
        var sourceOrderedZero = MeasureCase(interpreter, sourceOrderedPlan, options, input: 0, expectedValue: 0);

        AssertUsage(single.Usage, fuel: 17, loops: 1);
        AssertUsage(zero.Usage, fuel: 8, loops: 0);
        AssertUsage(two.Usage, fuel: 23, loops: 1);
        AssertUsage(twoZero.Usage, fuel: 10, loops: 0);
        AssertUsage(sourceOrdered.Usage, fuel: 23, loops: 1);
        AssertUsage(sourceOrderedZero.Usage, fuel: 10, loops: 0);

        var isolatedSingleLoopBytes = (single.AllocatedBytes - zero.AllocatedBytes) /
                                      (double)MeasurementIterations;
        Assert.InRange(isolatedSingleLoopBytes, 0, 8);

        AssertMultipleLoopAllocation(two, twoZero, "preassigned targets");
        AssertMultipleLoopAllocation(sourceOrdered, sourceOrderedZero, "earlier unassigned target");
    }

    private static void AssertMultipleLoopAllocation(
        Measurement loop,
        Measurement zero,
        string scenario)
    {
        var isolatedMultipleLoopBytes = (loop.AllocatedBytes - zero.AllocatedBytes) /
                                        (double)MeasurementIterations;
        Assert.True(
            isolatedMultipleLoopBytes < 700,
            $"One multi-assignment I64 loop execution ({scenario}) added {isolatedMultipleLoopBytes:F1} B " +
            $"(loop={loop.AllocatedBytes}, zero={zero.AllocatedBytes}). " +
            "Captured per-assignment slot predicates raise this isolated cost to about 824 B.");
    }

    private static void AssertUsage(Usage usage, long fuel, long loops)
    {
        Assert.Equal(fuel, usage.FuelUsed);
        Assert.Equal(loops, usage.LoopIterations);
        Assert.Equal(0, usage.AllocatedBytes);
        Assert.Equal(0, usage.HostCalls);
    }

    private static Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int input,
        long expectedValue)
    {
        var value = SandboxValue.FromInt32(input);
        _ = Measure(interpreter, plan, value, options, expectedValue, WarmupIterations);
        ForceGc();
        return Measure(interpreter, plan, value, options, expectedValue, MeasurementIterations);
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        long expectedValue,
        int iterations)
    {
        Usage? expectedUsage = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("I64 loop allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new Xunit.Sdk.XunitException(result.Error?.SafeMessage ?? "execution failed");
            }

            var actual = ((I64Value)result.Value!).Value;
            if (actual != expectedValue)
            {
                throw new Xunit.Sdk.XunitException($"expected {expectedValue}, got {actual}");
            }

            var usage = Usage.From(result.ResourceUsage);
            expectedUsage ??= usage;
            if (usage != expectedUsage.Value)
            {
                throw new Xunit.Sdk.XunitException("resource usage changed between identical executions");
            }
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            expectedUsage ?? default);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .Build());
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private const string SingleAssignmentModule = """
    {
      "id": "i64-single-assignment-allocation",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "total", "value": { "i64": 1 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [{
              "op": "set",
              "name": "total",
              "value": { "op": "add", "left": { "var": "total" }, "right": { "i64": 3 } }
            }]
          },
          { "op": "return", "value": { "var": "total" } }
        ]
      }]
    }
    """;

    private const string TwoAssignmentModule = """
    {
      "id": "i64-two-assignment-control",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "total", "value": { "i64": 1 } },
          { "op": "set", "name": "doubled", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [
              {
                "op": "set",
                "name": "total",
                "value": { "op": "add", "left": { "var": "total" }, "right": { "i64": 3 } }
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

    private const string SourceOrderedModule = """
    {
      "id": "i64-source-ordered-assignment-control",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [{ "name": "iterations", "type": "I32" }],
        "returnType": "I64",
        "body": [
          { "op": "set", "name": "source", "value": { "i64": 1 } },
          { "op": "set", "name": "doubled", "value": { "i64": 0 } },
          {
            "op": "forRange",
            "local": "i",
            "start": { "i32": 0 },
            "end": { "var": "iterations" },
            "body": [
              {
                "op": "set",
                "name": "total",
                "value": { "op": "add", "left": { "var": "source" }, "right": { "i64": 3 } }
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

    private readonly record struct Usage(
        long FuelUsed,
        long LoopIterations,
        long AllocatedBytes,
        int HostCalls)
    {
        public static Usage From(SandboxResourceUsage usage)
            => new(usage.FuelUsed, usage.LoopIterations, usage.AllocatedBytes, usage.HostCalls);
    }

    private readonly record struct Measurement(long AllocatedBytes, Usage Usage);
}
