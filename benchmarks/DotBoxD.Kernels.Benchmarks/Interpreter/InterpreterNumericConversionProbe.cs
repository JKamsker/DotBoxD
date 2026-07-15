using System.Diagnostics;
using System.Globalization;
using System.Text;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterNumericConversionProbe
{
    private const int WarmupIterations = 50_000;
    private const int MeasurementIterations = 100_000;
    private static readonly int[] ConversionCounts = [1, 4, 8];

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

        Console.WriteLine($"executions per row = {MeasurementIterations:N0}");
        Console.WriteLine("case                         ms       allocated B      B/op    F/L/A/H       checksum");
        foreach (var scenario in Scenarios())
        {
            foreach (var count in ConversionCounts)
            {
                var controlPlan = await PrepareAsync(host, policy, scenario, count, numeric: false);
                var numericPlan = await PrepareAsync(host, policy, scenario, count, numeric: true);
                _ = await MeasureAsync(interpreter, controlPlan, scenario.Input, options, WarmupIterations);
                _ = await MeasureAsync(interpreter, numericPlan, scenario.Input, options, WarmupIterations);

                ForceGc();
                var control = await MeasureAsync(
                    interpreter,
                    controlPlan,
                    scenario.Input,
                    options,
                    MeasurementIterations);
                ForceGc();
                var numeric = await MeasureAsync(
                    interpreter,
                    numericPlan,
                    scenario.Input,
                    options,
                    MeasurementIterations);

                Write($"{scenario.Name} unary x{count}", control);
                Write($"{scenario.Name} numeric x{count}", numeric);
                var delta = numeric.AllocatedBytes - control.AllocatedBytes;
                Console.WriteLine(
                    $"  delta {delta,17:N0} B = {delta / (double)MeasurementIterations,6:N1} B/exec" +
                    $" = {delta / (double)(MeasurementIterations * count),4:N1} B/conversion");
            }
        }
    }

    private static IReadOnlyList<Scenario> Scenarios()
        =>
        [
            new("I32->I64", "I32", "I64", "i64", "numeric.toI64", SandboxValue.FromInt32(-1_000)),
            new("I32->F64", "I32", "F64", "f64", "numeric.toF64", SandboxValue.FromInt32(-1_000)),
            new("I64->F64", "I64", "F64", "f64", "numeric.toF64", SandboxValue.FromInt64(-1_000))
        ];

    private static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        SandboxPolicy policy,
        Scenario scenario,
        int count,
        bool numeric)
    {
        var module = await host.ImportJsonAsync(CreateModuleJson(scenario, count, numeric));
        return await host.PrepareAsync(module, policy);
    }

    private static string CreateModuleJson(Scenario scenario, int count, bool numeric)
    {
        var body = new StringBuilder();
        body.Append($$"""
                  { "op": "set", "name": "seed", "value": { "{{scenario.TargetLiteral}}": 1000 } },

            """);
        for (var i = 0; i < count; i++)
        {
            var expression = numeric
                ? $$"""{ "call": "{{scenario.CallName}}", "args": [{ "var": "input" }] }"""
                : """{ "unary": "-", "operand": { "var": "seed" } }""";
            body.Append(CultureInfo.InvariantCulture, $$"""
                  { "op": "set", "name": "value{{i}}", "value": {{expression}} },

                """);
        }

        body.Append(CultureInfo.InvariantCulture, $$"""
                  { "op": "return", "value": { "var": "value{{count - 1}}" } }

            """);
        var lane = numeric ? "numeric" : "control";
        return $$"""
            {
              "id": "numeric-conversion-{{scenario.Name.ToLowerInvariant().Replace("->", "-")}}-{{lane}}-{{count}}",
              "version": "1.0.0",
              "functions": [{
                "id": "main",
                "visibility": "entrypoint",
                "parameters": [{ "name": "input", "type": "{{scenario.SourceType}}" }],
                "returnType": "{{scenario.TargetType}}",
                "body": [
            {{body}}    ]
              }]
            }
            """;
    }

    private static async Task<Measurement> MeasureAsync(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        int iterations)
    {
        double checksum = 0;
        SandboxResourceUsage? expectedUsage = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var result = await interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
            }

            checksum += result.Value switch
            {
                I64Value value => value.Value,
                F64Value value => value.Value,
                _ => throw new InvalidOperationException("unexpected result type")
            };
            expectedUsage ??= result.ResourceUsage;
            if (result.ResourceUsage != expectedUsage)
            {
                throw new InvalidOperationException("resource usage changed within the measurement");
            }
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage!.FuelUsed,
            expectedUsage.LoopIterations,
            expectedUsage.AllocatedBytes,
            expectedUsage.HostCalls);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.ElapsedMilliseconds,8:N1} {measurement.AllocatedBytes,17:N0}" +
            $" {measurement.AllocatedBytes / (double)MeasurementIterations,9:N1}" +
            $" {measurement.FuelUsed:N0}/{measurement.LoopIterations:N0}/" +
            $"{measurement.SandboxAllocatedBytes:N0}/{measurement.HostCalls:N0}" +
            $" {measurement.Checksum,14:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record Scenario(
        string Name,
        string SourceType,
        string TargetType,
        string TargetLiteral,
        string CallName,
        SandboxValue Input);

    private readonly record struct Measurement(
        double ElapsedMilliseconds,
        long AllocatedBytes,
        double Checksum,
        long FuelUsed,
        long LoopIterations,
        long SandboxAllocatedBytes,
        int HostCalls);
}
