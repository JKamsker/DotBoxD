using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class I32SingleAssignmentLoopAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Single_assignment_loop_does_not_allocate_an_assignment_array()
    {
        using var host = SandboxTestHost.Create();
        var singlePlan = await PrepareAsync(host, SingleAssignmentModule);
        var twoPlan = await PrepareAsync(host, TwoAssignmentModule);
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        var single = MeasureCase(interpreter, singlePlan, options, input: 1, expectedValue: 3);
        var zero = MeasureCase(interpreter, singlePlan, options, input: 0, expectedValue: 0);
        var two = MeasureCase(interpreter, twoPlan, options, input: 1, expectedValue: 6);

        Assert.Equal(19, single.Usage.FuelUsed);
        Assert.Equal(1, single.Usage.LoopIterations);
        Assert.Equal(8, zero.Usage.FuelUsed);
        Assert.Equal(0, zero.Usage.LoopIterations);
        Assert.Equal(25, two.Usage.FuelUsed);
        Assert.Equal(1, two.Usage.LoopIterations);
        Assert.Equal(0, single.Usage.AllocatedBytes);
        Assert.Equal(0, zero.Usage.AllocatedBytes);
        Assert.Equal(0, two.Usage.AllocatedBytes);
        Assert.Equal(0, single.Usage.HostCalls);
        Assert.Equal(0, zero.Usage.HostCalls);
        Assert.Equal(0, two.Usage.HostCalls);

        var singleLoopBytes = (single.AllocatedBytes - zero.AllocatedBytes) /
                              (double)MeasurementIterations;
        Assert.True(
            singleLoopBytes < 80,
            $"One single-assignment loop execution added {singleLoopBytes:F1} B " +
            $"(single={single.AllocatedBytes}, zero={zero.AllocatedBytes}). " +
            "The legacy one-element AssignmentPlan[] raises this isolated cost to about 96 B.");
    }

    private static Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int input,
        int expectedValue)
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
        int expectedValue,
        int iterations)
    {
        long checksum = 0;
        Usage? expectedUsage = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("loop allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new Xunit.Sdk.XunitException(result.Error?.SafeMessage ?? "execution failed");
            }

            var actual = ((I32Value)result.Value!).Value;
            if (actual != expectedValue)
            {
                throw new Xunit.Sdk.XunitException($"expected {expectedValue}, got {actual}");
            }

            checksum += actual;
            var usage = Usage.From(result.ResourceUsage);
            expectedUsage ??= usage;
            if (usage != expectedUsage.Value)
            {
                throw new Xunit.Sdk.XunitException("resource usage changed between identical executions");
            }
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
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
      "id": "i32-single-assignment-allocation",
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

    private const string TwoAssignmentModule = """
    {
      "id": "i32-two-assignment-control",
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

    private readonly record struct Usage(
        long FuelUsed,
        long LoopIterations,
        long AllocatedBytes,
        int HostCalls)
    {
        public static Usage From(SandboxResourceUsage usage)
            => new(usage.FuelUsed, usage.LoopIterations, usage.AllocatedBytes, usage.HostCalls);
    }

    private readonly record struct Measurement(long AllocatedBytes, long Checksum, Usage Usage);
}
