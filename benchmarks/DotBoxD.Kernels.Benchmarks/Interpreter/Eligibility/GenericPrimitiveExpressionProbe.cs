using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class GenericPrimitiveExpressionProbe
{
    private static readonly int[] Depths = [8, 16, 32, 64, 96];
    private static readonly bool[] Associations = [true, false];

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var policy = SandboxPolicyBuilder.Create()
            .AllowPureComputation()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var interpreter = new SandboxInterpreter();
        var options = GenericPrimitiveExpressionMeasurement.Options();
        var cases = await PrepareDepthCasesAsync(host, policy);

        foreach (var benchmarkCase in cases)
        {
            GenericPrimitiveExpressionMeasurement.Warm(
                interpreter,
                benchmarkCase.LeftOperand,
                options,
                expected: true,
                benchmarkCase.ExpectedFuel);
            GenericPrimitiveExpressionMeasurement.Warm(
                interpreter,
                benchmarkCase.RightOperand,
                options,
                expected: true,
                benchmarkCase.ExpectedFuel);
        }

        Console.WriteLine("Generic primitive-expression probe");
        Console.WriteLine(
            $"iterations/sample = {GenericPrimitiveExpressionMeasurement.MeasurementIterations:N0}; " +
            $"samples = {GenericPrimitiveExpressionMeasurement.Samples}");
        Console.WriteLine(
            "depth  shape  left-op ns  right-op ns  left/right  left B/op  right B/op  fuel");
        foreach (var benchmarkCase in cases)
        {
            var left = GenericPrimitiveExpressionMeasurement.MeasureMedian(
                interpreter,
                benchmarkCase.LeftOperand,
                options,
                expected: true,
                benchmarkCase.ExpectedFuel);
            var right = GenericPrimitiveExpressionMeasurement.MeasureMedian(
                interpreter,
                benchmarkCase.RightOperand,
                options,
                expected: true,
                benchmarkCase.ExpectedFuel);
            Console.WriteLine(
                $"{benchmarkCase.Depth,5} {benchmarkCase.Shape,6} " +
                $"{left.NanosecondsPerOperation,11:N1} " +
                $"{right.NanosecondsPerOperation,12:N1} " +
                $"{left.NanosecondsPerOperation / right.NanosecondsPerOperation,11:N2} " +
                $"{left.BytesPerOperation,10:N1} {right.BytesPerOperation,11:N1} " +
                $"{left.Fuel,5:N0}");
        }

        await GenericPrimitiveExpressionProbeControls.RunAsync(
            host,
            policy,
            interpreter,
            options);
    }

    private static async Task<DepthCase[]> PrepareDepthCasesAsync(
        SandboxHost host,
        SandboxPolicy policy)
    {
        var cases = new List<DepthCase>(Depths.Length * Associations.Length);
        foreach (var depth in Depths)
        {
            foreach (var leftDeep in Associations)
            {
                cases.Add(new DepthCase(
                    depth,
                    leftDeep ? "left" : "right",
                    await host.PrepareAsync(
                        GenericPrimitiveExpressionProbeModules.F64Comparison(
                            depth,
                            leftDeep,
                            arithmeticOnLeft: true),
                        policy),
                    await host.PrepareAsync(
                        GenericPrimitiveExpressionProbeModules.F64Comparison(
                            depth,
                            leftDeep,
                            arithmeticOnLeft: false),
                        policy),
                    GenericPrimitiveExpressionProbeModules.ExpectedComparisonFuel(depth)));
            }
        }

        return [.. cases];
    }

    private readonly record struct DepthCase(
        int Depth,
        string Shape,
        ExecutionPlan LeftOperand,
        ExecutionPlan RightOperand,
        long ExpectedFuel);
}
