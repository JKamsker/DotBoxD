using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class GenericPrimitiveExpressionProbeControls
{
    public static async Task RunAsync(
        SandboxHost host,
        SandboxPolicy policy,
        SandboxInterpreter interpreter,
        SandboxExecutionOptions options)
    {
        await RunHotPathControlsAsync(host, policy, interpreter, options);
        await RunNegativeControlsAsync(host, policy, interpreter, options);
        await RunSemanticControlsAsync(host, policy, interpreter);
    }

    private static async Task RunHotPathControlsAsync(
        SandboxHost host,
        SandboxPolicy policy,
        SandboxInterpreter interpreter,
        SandboxExecutionOptions options)
    {
        const int depth = 96;
        Console.WriteLine();
        Console.WriteLine("control                         ns/op      B/op   fuel/hosts");
        await WriteControlAsync(host, policy, interpreter, options, "I32 left depth 96",
            GenericPrimitiveExpressionProbeModules.I32Tree(depth, true), 97, 195, 0);
        await WriteControlAsync(host, policy, interpreter, options, "I32 right depth 96",
            GenericPrimitiveExpressionProbeModules.I32Tree(depth, false), 97, 195, 0);
        await WriteControlAsync(host, policy, interpreter, options, "I64 left operand d96",
            GenericPrimitiveExpressionProbeModules.I64Comparison(depth, true), true,
            GenericPrimitiveExpressionProbeModules.ExpectedComparisonFuel(depth), 0);
        await WriteControlAsync(host, policy, interpreter, options, "I64 right operand d96",
            GenericPrimitiveExpressionProbeModules.I64Comparison(depth, false), true,
            GenericPrimitiveExpressionProbeModules.ExpectedComparisonFuel(depth), 0);
        await WriteControlAsync(host, policy, interpreter, options, "shallow Bool",
            GenericPrimitiveExpressionProbeModules.ShallowBool(), true, 5, 0);
        await WriteControlAsync(host, policy, interpreter, options, "shallow intrinsic call",
            GenericPrimitiveExpressionProbeModules.ShallowIntrinsicCall(), 1D, 6, 1);
        await WriteControlAsync(host, policy, interpreter, options, "eligible I32 local call",
            GenericPrimitiveExpressionProbeModules.EligibleLocalCall(), 7, 6, 0);
        await WriteControlAsync(host, policy, interpreter, options, "F64 right d16, left operand",
            GenericPrimitiveExpressionProbeModules.F64Comparison(16, false, true), true, 39, 0, 200_000);
        await WriteControlAsync(host, policy, interpreter, options, "F64 right d16, right operand",
            GenericPrimitiveExpressionProbeModules.F64Comparison(16, false, false), true, 39, 0, 200_000);
    }

    private static async Task RunNegativeControlsAsync(
        SandboxHost host,
        SandboxPolicy policy,
        SandboxInterpreter interpreter,
        SandboxExecutionOptions options)
    {
        const int depth = 96;
        var fuel = GenericPrimitiveExpressionProbeModules.ExpectedIntrinsicComparisonFuel(depth);
        Console.WriteLine();
        Console.WriteLine("deep ineligible control         ns/op      B/op   fuel/hosts");
        await WriteControlAsync(host, policy, interpreter, options, "call left/left operand",
            GenericPrimitiveExpressionProbeModules.F64IntrinsicComparison(depth, true, true),
            true, fuel, 1);
        await WriteControlAsync(host, policy, interpreter, options, "call right/left operand",
            GenericPrimitiveExpressionProbeModules.F64IntrinsicComparison(depth, false, true),
            true, fuel, 1);
        await WriteControlAsync(host, policy, interpreter, options, "call left/right operand",
            GenericPrimitiveExpressionProbeModules.F64IntrinsicComparison(depth, true, false),
            true, fuel, 1);
        await WriteControlAsync(host, policy, interpreter, options, "call right/right operand",
            GenericPrimitiveExpressionProbeModules.F64IntrinsicComparison(depth, false, false),
            true, fuel, 1);
        var conversionFuel =
            GenericPrimitiveExpressionProbeModules.ExpectedConversionComparisonFuel(depth);
        await WriteControlAsync(host, policy, interpreter, options, "conversion left/left operand",
            GenericPrimitiveExpressionProbeModules.F64ConversionComparison(depth, true),
            true, conversionFuel, 0);
        await WriteControlAsync(host, policy, interpreter, options, "conversion right/left operand",
            GenericPrimitiveExpressionProbeModules.F64ConversionComparison(depth, false),
            true, conversionFuel, 0);
        await WriteControlAsync(host, policy, interpreter, options, "mixed call, pure left",
            GenericPrimitiveExpressionProbeModules.MixedF64Sibling(
                depth, pureOnLeft: true, useConversion: false),
            depth + 2D, fuel, 1);
        await WriteControlAsync(host, policy, interpreter, options, "mixed call, pure right",
            GenericPrimitiveExpressionProbeModules.MixedF64Sibling(
                depth, pureOnLeft: false, useConversion: false),
            depth + 2D, fuel, 1);
        await WriteControlAsync(host, policy, interpreter, options, "mixed conversion, pure left",
            GenericPrimitiveExpressionProbeModules.MixedF64Sibling(
                depth, pureOnLeft: true, useConversion: true),
            depth + 2D, conversionFuel, 0);
        await WriteControlAsync(host, policy, interpreter, options, "mixed conversion, pure right",
            GenericPrimitiveExpressionProbeModules.MixedF64Sibling(
                depth, pureOnLeft: false, useConversion: true),
            depth + 2D, conversionFuel, 0);
        await WriteSaturatedControlAsync(host, policy, interpreter, options, depth);
        await GenericPrimitiveExpressionColdControls.RunAsync(host, policy, options);
    }

    private static async Task WriteSaturatedControlAsync(
        SandboxHost host,
        SandboxPolicy policy,
        SandboxInterpreter interpreter,
        SandboxExecutionOptions options,
        int depth)
    {
        var plan = await host.PrepareAsync(
            GenericPrimitiveExpressionProbeModules.SaturatedCache(depth),
            policy);
        for (var i = 0; i < 4; i++)
        {
            GenericPrimitiveExpressionMeasurement.Warm(
                interpreter,
                plan,
                options,
                expected: true,
                fuel: 8,
                entrypoint: $"seed{i}");
        }

        var measurement = GenericPrimitiveExpressionMeasurement.MeasureMedian(
            interpreter,
            plan,
            options,
            expected: true,
            GenericPrimitiveExpressionProbeModules.ExpectedIntrinsicComparisonFuel(depth),
            hostCalls: 1,
            entrypoint: "overflow");
        Console.WriteLine(
            $"{"cache-full fifth root",-30} {measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.BytesPerOperation,9:N1} {measurement.Fuel,5:N0}/1");
    }

    private static async Task WriteControlAsync(
        SandboxHost host,
        SandboxPolicy policy,
        SandboxInterpreter interpreter,
        SandboxExecutionOptions options,
        string name,
        SandboxModule module,
        object expected,
        long fuel,
        int hostCalls,
        int measurementIterations = GenericPrimitiveExpressionMeasurement.MeasurementIterations)
    {
        var plan = await host.PrepareAsync(module, policy);
        GenericPrimitiveExpressionMeasurement.Warm(
            interpreter, plan, options, expected, fuel, hostCalls);
        var measurement = GenericPrimitiveExpressionMeasurement.MeasureMedian(
            interpreter,
            plan,
            options,
            expected,
            fuel,
            hostCalls,
            measurementIterations: measurementIterations);
        Console.WriteLine(
            $"{name,-30} {measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.BytesPerOperation,9:N1} {measurement.Fuel,5:N0}/{hostCalls:N0}");
    }

    private static async Task RunSemanticControlsAsync(
        SandboxHost host,
        SandboxPolicy policy,
        SandboxInterpreter interpreter)
    {
        const int depth = 8;
        var tracedPlan = await host.PrepareAsync(
            GenericPrimitiveExpressionProbeModules.F64Comparison(depth, true), policy);
        GenericPrimitiveExpressionMeasurement.RequireValue(
            GenericPrimitiveExpressionMeasurement.Execute(
                interpreter,
                tracedPlan,
                GenericPrimitiveExpressionMeasurement.Options()),
            true);
        GenericPrimitiveExpressionMeasurement.RequireValue(
            GenericPrimitiveExpressionMeasurement.Execute(
                interpreter,
                tracedPlan,
                GenericPrimitiveExpressionMeasurement.Options()),
            true);
        var traced = GenericPrimitiveExpressionMeasurement.Execute(
            interpreter,
            tracedPlan,
            GenericPrimitiveExpressionMeasurement.Options(enableTrace: true));
        GenericPrimitiveExpressionMeasurement.RequireValue(traced, true);
        var traceCount = traced.AuditEvents.Count(audit => audit.Kind == "DebugTrace");
        if (traceCount != 22 || traced.ResourceUsage.FuelUsed != 23)
        {
            throw new InvalidOperationException(
                $"trace control changed: traces={traceCount}, fuel={traced.ResourceUsage.FuelUsed}");
        }

        var faultPlan = await host.PrepareAsync(
            GenericPrimitiveExpressionProbeModules.F64Fault(depth), policy);
        SandboxExecutionResult fault = null!;
        for (var i = 0; i < 3; i++)
        {
            fault = GenericPrimitiveExpressionMeasurement.Execute(
                interpreter,
                faultPlan,
                GenericPrimitiveExpressionMeasurement.Options());
        }
        if (fault.Succeeded ||
            fault.Error is not
            { Code: SandboxErrorCode.InvalidInput, SafeMessage: "f64 result must be finite" } ||
            fault.ResourceUsage.FuelUsed !=
                GenericPrimitiveExpressionProbeModules.ExpectedFaultFuel(depth))
        {
            throw new InvalidOperationException(
                $"fault control changed: success={fault.Succeeded}, " +
                $"error={fault.Error?.SafeMessage}, fuel={fault.ResourceUsage.FuelUsed}");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"semantic controls: trace events/fuel={traceCount}/{traced.ResourceUsage.FuelUsed}; " +
            $"fault={fault.Error.SafeMessage}; fault fuel={fault.ResourceUsage.FuelUsed}");
    }
}
