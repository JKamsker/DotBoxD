using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class WhileI32SingleAssignmentLoopAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Single_assignment_while_does_not_allocate_an_assignment_array()
    {
        using var host = SandboxTestHost.Create();
        var singlePlan = await PrepareAsync(host, SingleAssignmentModule);
        var noWhilePlan = await PrepareAsync(host, NoWhileControlModule);
        var twoAssignmentPlan = await PrepareAsync(host, TwoAssignmentModule);
        var interpreter = new SandboxInterpreter();
        var options = CreateOptions();

        var single = MeasureCase(interpreter, singlePlan, options, input: 1, expectedValue: 1);
        var zero = MeasureCase(interpreter, singlePlan, options, input: 0, expectedValue: 0);
        var noWhile = MeasureCase(interpreter, noWhilePlan, options, input: 0, expectedValue: 0);
        var two = MeasureCase(interpreter, twoAssignmentPlan, options, input: 1, expectedValue: 2);

        AssertUsage(single, fuel: 21, loops: 1);
        AssertUsage(zero, fuel: 9, loops: 0);
        AssertUsage(noWhile, fuel: 5, loops: 0);
        AssertUsage(two, fuel: 27, loops: 1);
        Assert.Equal(MeasurementIterations, single.Checksum);
        Assert.Equal(0, zero.Checksum);
        Assert.Equal(0, noWhile.Checksum);
        Assert.Equal(2L * MeasurementIterations, two.Checksum);

        var singleWhileOverhead = (single.AllocatedBytes - noWhile.AllocatedBytes) /
                                  (double)MeasurementIterations;
        var zeroWhileOverhead = (zero.AllocatedBytes - noWhile.AllocatedBytes) /
                                (double)MeasurementIterations;
        var twoAssignmentBytesPerExecution = two.AllocatedBytes /
                                             (double)MeasurementIterations;
        // The isolated scalar-plan overhead is about 320 B/op; the legacy 40-byte plan array raises it
        // to about 360 B/op. The lazy audit envelope lowers both absolute totals by 64 B/op. Leave room
        // for full-suite runtime bookkeeping while keeping the scalar-plan bands separate.
        Assert.InRange(singleWhileOverhead, 300, 340);
        Assert.InRange(zeroWhileOverhead, 300, 340);
        Assert.InRange(twoAssignmentBytesPerExecution, 1_375, 1_425);
    }

    [Fact]
    public async Task Unsupported_single_statement_while_falls_back_to_the_general_interpreter()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, BreakFallbackModule);

        var result = await new SandboxInterpreter().ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(1),
            CreateOptions(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(0, ((I32Value)result.Value!).Value);
        Assert.Equal(1, result.ResourceUsage.LoopIterations);
    }

    private static SandboxExecutionOptions CreateOptions()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

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
                throw new InvalidOperationException("while allocation test unexpectedly became asynchronous");
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

    private static void AssertUsage(Measurement measurement, long fuel, long loops)
    {
        Assert.Equal(fuel, measurement.Usage.FuelUsed);
        Assert.Equal(loops, measurement.Usage.LoopIterations);
        Assert.Equal(0, measurement.Usage.AllocatedBytes);
        Assert.Equal(0, measurement.Usage.HostCalls);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private const string SingleAssignmentModule = """
    {
      "id": "while-i32-single-assignment-allocation",
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
      "id": "while-i32-no-loop-control",
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
      "id": "while-i32-two-assignment-control",
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

    private const string BreakFallbackModule = """
    {
      "id": "while-i32-break-fallback",
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
            "body": [{ "op": "break" }]
          },
          { "op": "return", "value": { "var": "counter" } }
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
