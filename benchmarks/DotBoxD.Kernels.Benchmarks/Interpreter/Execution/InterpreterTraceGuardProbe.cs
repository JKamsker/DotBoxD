using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterTraceGuardProbe
{
    private const int ExpressionStatements = 32;
    private const int WarmupIterations = 5_000;
    private const int Iterations = 250_000;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder => builder.UseInterpreter());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var module = await host.ImportJsonAsync(CreateModuleJson());
        var plan = await host.PrepareAsync(module, policy);
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        _ = Measure(interpreter, plan, options, WarmupIterations);
        ForceGc();
        var measurement = Measure(interpreter, plan, options, Iterations);

        Console.WriteLine("Interpreter disabled-trace guard probe");
        Console.WriteLine($"executions = {Iterations:N0}; statements/execution = {ExpressionStatements + 1:N0}");
        Console.WriteLine("case                        total ms       ns/statement    allocated B       B/op");
        Console.WriteLine(
            $"tracing disabled            {measurement.ElapsedMilliseconds,9:N1} " +
            $"{measurement.NanosecondsPerStatement,18:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{measurement.AllocatedBytes / (double)Iterations,10:N1}");
    }

    private static TraceMeasurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int iterations)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                options,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("trace guard probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded ||
                !ReferenceEquals(result.Value, SandboxValue.Unit) ||
                result.AuditEvents.Count != 0 ||
                result.ResourceUsage.FuelUsed != ((ExpressionStatements + 1) * 2) + 1)
            {
                throw new InvalidOperationException(
                    result.Error?.SafeMessage ??
                    $"trace guard invariant changed: value={result.Value?.Type}, " +
                    $"audit={result.AuditEvents.Count}, fuel={result.ResourceUsage.FuelUsed}");
            }

            checksum++;
        }

        watch.Stop();
        if (checksum != iterations)
        {
            throw new InvalidOperationException($"expected checksum {iterations}, got {checksum}");
        }

        return new TraceMeasurement(
            watch.Elapsed.TotalMilliseconds,
            watch.Elapsed.TotalNanoseconds / (iterations * (ExpressionStatements + 1L)),
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static string CreateModuleJson()
    {
        const string expression = """{ "op": "expr", "value": { "unit": true } }""";
        var expressions = string.Join(",\n", Enumerable.Repeat(expression, ExpressionStatements));
        return $$"""
        {
          "id": "interpreter-trace-guard",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "Unit",
            "body": [
              {{expressions}},
              { "op": "return", "value": { "unit": true } }
            ]
          }]
        }
        """;
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct TraceMeasurement(
        double ElapsedMilliseconds,
        double NanosecondsPerStatement,
        long AllocatedBytes);
}
