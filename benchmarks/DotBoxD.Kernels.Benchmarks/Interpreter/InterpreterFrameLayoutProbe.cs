using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterFrameLayoutProbe
{
    private const int WarmupIterations = 1_000;
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
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        Console.WriteLine($"interpreter frame-layout executions = {Iterations:N0}");
        Console.WriteLine("case                 total ms       B/op   checksum");
        await RunCaseAsync(host, interpreter, options, policy, "parameter return", ParameterReturnJson());
        await RunCaseAsync(host, interpreter, options, policy, "eight local chain", EightLocalChainJson());
    }

    private static async Task RunCaseAsync(
        SandboxHost host,
        SandboxInterpreter interpreter,
        SandboxExecutionOptions options,
        SandboxPolicy policy,
        string name,
        string moduleJson)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        var input = SandboxValue.FromInt32(1);

        _ = await MeasureAsync(interpreter, plan, input, options, WarmupIterations);
        ForceGc();
        var measurement = await MeasureAsync(interpreter, plan, input, options, Iterations);
        Console.WriteLine(
            $"{name,-20} {measurement.ElapsedMilliseconds,8:N1} {measurement.Bytes / (double)Iterations,10:N1} {measurement.Checksum,10:N0}");
    }

    private static async Task<Measurement> MeasureAsync(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        int iterations)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var result = await interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
            }

            checksum += ((I32Value)result.Value!).Value;
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string ParameterReturnJson()
        => """
        {
          "id": "frame-layout-parameter",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "I32" }],
            "returnType": "I32",
            "body": [{ "op": "return", "value": { "var": "value" } }]
          }]
        }
        """;

    private static string EightLocalChainJson()
        => """
        {
          "id": "frame-layout-locals",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "value", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "a", "value": { "op": "add", "left": { "var": "value" }, "right": { "i32": 1 } } },
              { "op": "set", "name": "b", "value": { "op": "add", "left": { "var": "a" }, "right": { "i32": 2 } } },
              { "op": "set", "name": "c", "value": { "op": "add", "left": { "var": "b" }, "right": { "i32": 3 } } },
              { "op": "set", "name": "d", "value": { "op": "add", "left": { "var": "c" }, "right": { "i32": 4 } } },
              { "op": "set", "name": "e", "value": { "op": "add", "left": { "var": "d" }, "right": { "i32": 5 } } },
              { "op": "set", "name": "f", "value": { "op": "add", "left": { "var": "e" }, "right": { "i32": 6 } } },
              { "op": "set", "name": "g", "value": { "op": "add", "left": { "var": "f" }, "right": { "i32": 7 } } },
              { "op": "set", "name": "h", "value": { "op": "add", "left": { "var": "g" }, "right": { "i32": 8 } } },
              { "op": "return", "value": { "var": "h" } }
            ]
          }]
        }
        """;

    private readonly record struct Measurement(double ElapsedMilliseconds, long Bytes, long Checksum);
}
