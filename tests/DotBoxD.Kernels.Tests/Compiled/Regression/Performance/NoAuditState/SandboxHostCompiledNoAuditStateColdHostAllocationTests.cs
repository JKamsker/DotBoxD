namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class SandboxHostCompiledNoAuditStateColdHostAllocationTests
{
    private const int WarmupIterations = 1_000;
    private const int MeasuredIterations = 100_000;
    private const int SampleCount = 3;
    private const long MaximumBytesPerHost = 4_856;

    private static readonly Action<SandboxHostBuilder> InterpreterConfiguration =
        static builder => builder.UseInterpreter();

    [Fact]
    public void Explicit_interpreter_cold_hosts_do_not_allocate_more_than_default_and_both_remain_bounded()
    {
        _ = MeasureDefaultHosts(WarmupIterations);
        _ = MeasureInterpreterHosts(WarmupIterations);
        ForceGc();

        var defaultAllocated = long.MaxValue;
        var interpreterAllocated = long.MaxValue;
        for (var sample = 0; sample < SampleCount; sample++)
        {
            defaultAllocated = Math.Min(defaultAllocated, MeasureDefaultHosts(MeasuredIterations));
            interpreterAllocated = Math.Min(
                interpreterAllocated,
                MeasureInterpreterHosts(MeasuredIterations));
        }

        Console.WriteLine(
            $"cold host construction: default={defaultAllocated / (double)MeasuredIterations:N3} B/host, " +
            $"interpreter={interpreterAllocated / (double)MeasuredIterations:N3} B/host.");
        var defaultBytesPerHost = defaultAllocated / MeasuredIterations;
        var interpreterBytesPerHost = interpreterAllocated / MeasuredIterations;
        Assert.InRange(defaultBytesPerHost, 0, MaximumBytesPerHost);
        Assert.InRange(interpreterBytesPerHost, 0, MaximumBytesPerHost);

        // Dynamic PGO can optimize the two overload call sites independently, so the
        // explicit path may stack-allocate more of its short-lived setup first. The
        // regression contract is that selecting the interpreter adds no allocation.
        Assert.InRange(interpreterBytesPerHost, 0, defaultBytesPerHost);
    }

    private static long MeasureDefaultHosts(int iterations)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            using var host = SandboxHost.Create();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static long MeasureInterpreterHosts(int iterations)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            using var host = SandboxHost.Create(InterpreterConfiguration);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
