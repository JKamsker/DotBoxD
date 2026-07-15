using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LazyAudit;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class InterpreterLazyAuditAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Pure_suppressed_success_omits_exact_run_id_and_audit_sink_allocations()
    {
        using var host = SandboxTestHost.Create();
        var lazyPlan = await InterpreterLazyAuditTestSupport.PreparePureAsync(host);
        var forcedFullPlan = InterpreterLazyAuditTestSupport.WithBindingReferences(
            lazyPlan,
            InterpreterLazyAuditTestSupport.References("math.abs"));
        var missingMetadataPlan = InterpreterLazyAuditTestSupport.WithBindingReferences(
            lazyPlan,
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal));
        var interpreter = new SandboxInterpreter();
        var input = SandboxValue.FromInt32(7);
        var defaultOptions = InterpreterLazyAuditTestSupport.SuppressedOptions();
        var explicitOptions = InterpreterLazyAuditTestSupport.SuppressedOptions(SandboxRunId.New());

        var lazyDefault = MeasureCase(interpreter, lazyPlan, input, defaultOptions);
        var fullDefault = MeasureCase(interpreter, forcedFullPlan, input, defaultOptions);
        var missingDefault = MeasureCase(interpreter, missingMetadataPlan, input, defaultOptions);
        var lazyExplicit = MeasureCase(interpreter, lazyPlan, input, explicitOptions);
        var fullExplicit = MeasureCase(interpreter, forcedFullPlan, input, explicitOptions);

        AssertEquivalent(lazyDefault, fullDefault);
        AssertEquivalent(lazyDefault, missingDefault);
        AssertEquivalent(lazyExplicit, fullExplicit);
        AssertAllocationDifference(fullDefault, lazyDefault, expectedBytesPerExecution: 64);
        AssertAllocationDifference(fullExplicit, lazyExplicit, expectedBytesPerExecution: 32);
        AssertAllocationDifference(fullDefault, missingDefault, expectedBytesPerExecution: 0);
    }

    private static Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options)
    {
        _ = Measure(interpreter, plan, input, options, WarmupIterations);
        ForceGc();
        return Measure(interpreter, plan, input, options, MeasurementIterations);
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        int iterations)
    {
        long checksum = 0;
        SandboxResourceUsage? expectedUsage = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("lazy-audit allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded || result.Value is not I32Value value || value.Value != 7)
            {
                throw new Xunit.Sdk.XunitException(result.Error?.SafeMessage ?? "unexpected execution result");
            }

            if (result.AuditEvents.Count != 0)
            {
                throw new Xunit.Sdk.XunitException("suppressed successful execution emitted audit events");
            }

            checksum += value.Value;
            expectedUsage ??= result.ResourceUsage;
            if (result.ResourceUsage != expectedUsage)
            {
                throw new Xunit.Sdk.XunitException("resource usage changed between identical executions");
            }
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage ?? throw new InvalidOperationException("measurement executed no iterations"));
    }

    private static void AssertEquivalent(Measurement expected, Measurement actual)
    {
        Assert.Equal(expected.Checksum, actual.Checksum);
        Assert.Equal(expected.ResourceUsage, actual.ResourceUsage);
    }

    private static void AssertAllocationDifference(
        Measurement full,
        Measurement lazy,
        int expectedBytesPerExecution)
    {
        var actual = (full.AllocatedBytes - lazy.AllocatedBytes) / (double)MeasurementIterations;
        Assert.True(
            Math.Abs(actual - expectedBytesPerExecution) <= 0.1,
            $"Expected {expectedBytesPerExecution:F1} B/execution, observed {actual:F3} B/execution.");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        long AllocatedBytes,
        long Checksum,
        SandboxResourceUsage ResourceUsage);
}
