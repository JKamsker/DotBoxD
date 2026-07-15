using System.Text;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class InterpreterNumericConversionAllocationTests
{
    private const int ConversionCount = 8;
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Theory]
    [InlineData("numeric.toI64", "I32", "I64", "i64", false)]
    [InlineData("numeric.toF64", "I32", "F64", "f64", false)]
    [InlineData("numeric.toF64", "I64", "F64", "f64", true)]
    public async Task Numeric_conversion_does_not_allocate_an_operand_array(
        string conversion,
        string sourceType,
        string targetType,
        string targetLiteral,
        bool sourceIsI64)
    {
        using var host = SandboxTestHost.Create();
        var scenario = new Scenario(conversion, sourceType, targetType, targetLiteral, sourceIsI64);
        var controlPlan = await PrepareAsync(host, scenario, numeric: false);
        var numericPlan = await PrepareAsync(host, scenario, numeric: true);
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        _ = Measure(interpreter, controlPlan, scenario, options, WarmupIterations);
        _ = Measure(interpreter, numericPlan, scenario, options, WarmupIterations);
        var control = Measure(interpreter, controlPlan, scenario, options, MeasurementIterations);
        var numeric = Measure(interpreter, numericPlan, scenario, options, MeasurementIterations);

        Assert.Equal(control.Checksum, numeric.Checksum);
        Assert.Equal(control.Usage, numeric.Usage);
        var bytesPerConversion = (numeric.AllocatedBytes - control.AllocatedBytes) /
                                 (double)(MeasurementIterations * ConversionCount);
        Assert.True(
            bytesPerConversion < 16,
            $"{sourceType}->{targetType} allocated {bytesPerConversion:F1} B/conversion " +
            $"(numeric={numeric.AllocatedBytes}, control={control.AllocatedBytes}). " +
            "A one-element SandboxValue[] costs 32 B and must not be allocated per conversion.");
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        Scenario scenario,
        bool numeric)
    {
        var module = await host.ImportJsonAsync(ModuleJson(scenario, numeric));
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxAllocatedBytes(long.MaxValue)
                .Build());
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        Scenario scenario,
        SandboxExecutionOptions options,
        int iterations)
    {
        ForceGc();
        double checksum = 0;
        SandboxResourceUsage? expectedUsage = null;
        var input = scenario.SourceIsI64
            ? SandboxValue.FromInt64(-1_000)
            : SandboxValue.FromInt32(-1_000);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(
                plan,
                "main",
                input,
                options,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "numeric-conversion allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            checksum += result.Value switch
            {
                I64Value value => value.Value,
                F64Value value => value.Value,
                _ => throw new Xunit.Sdk.XunitException("unexpected numeric conversion result")
            };
            expectedUsage ??= result.ResourceUsage;
            Assert.Equal(expectedUsage, result.ResourceUsage);
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage!);
    }

    private static string ModuleJson(Scenario scenario, bool numeric)
    {
        var body = new StringBuilder();
        body.AppendLine($$"""{ "op": "set", "name": "seed", "value": { "{{scenario.TargetLiteral}}": 1000 } },""");
        for (var i = 0; i < ConversionCount; i++)
        {
            var expression = numeric
                ? $$"""{ "call": "{{scenario.Conversion}}", "args": [{ "var": "input" }] }"""
                : """{ "unary": "-", "operand": { "var": "seed" } }""";
            body.AppendLine($$"""{ "op": "set", "name": "value{{i}}", "value": {{expression}} },""");
        }

        body.AppendLine($$"""{ "op": "return", "value": { "var": "value{{ConversionCount - 1}}" } }""");
        var lane = numeric ? "numeric" : "control";
        return $$"""
        {
          "id": "interpreter-numeric-allocation-{{scenario.SourceType.ToLowerInvariant()}}-{{scenario.TargetType.ToLowerInvariant()}}-{{lane}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "input", "type": "{{scenario.SourceType}}" }],
            "returnType": "{{scenario.TargetType}}",
            "body": [{{body}}]
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

    private readonly record struct Measurement(
        long AllocatedBytes,
        double Checksum,
        SandboxResourceUsage Usage);

    private sealed record Scenario(
        string Conversion,
        string SourceType,
        string TargetType,
        string TargetLiteral,
        bool SourceIsI64);
}
