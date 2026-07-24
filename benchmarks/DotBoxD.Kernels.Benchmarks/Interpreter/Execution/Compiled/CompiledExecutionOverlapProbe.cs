using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Compiler.Emitters;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class CompiledExecutionOverlapProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 1_000_000;
    private const int ColdPrimaryIterations = 100_000;
    private const double MaximumPooledBytesPerOperation = 200D;
    private const double MinimumFreshBytesPerOperation = 500D;
    private const double MaximumFreshBytesPerOperation = 520D;
    private const double ExpectedStateSavingsBytesPerOperation = 320D;
    private const double AllocationNoiseBytesPerOperation = 2D;

    public static async Task RunAsync(
        SandboxHost host,
        ExecutionPlan plan,
        CompiledExecutionInvariant expected)
    {
        var executable = await CreateEligibilityExecutableAsync(plan);
        using var primary = host.TryAcquireCompiledNoAuditState(
            plan,
            "main",
            executable,
            CompiledExecutionEnvelopeProbe.SuppressedOptions,
            CancellationToken.None,
            suppliedState: null,
            useAsyncWorker: false);
        if (!primary.IsAcquired)
        {
            throw new InvalidOperationException("compiled overlap probe could not hold the primary state lane");
        }

        _ = MeasureColdPrimaryAdmissions(plan, WarmupIterations);
        CompiledExecutionEnvelopeProbe.ForceGc();
        var coldPrimary = MeasureColdPrimaryAdmissions(plan, ColdPrimaryIterations);
        CompiledExecutionEnvelopeProbe.ForceGc();
        var activation = CompiledExecutionEnvelopeProbe.Measure(
            host, plan, expected, CancellationToken.None, iterations: 1);
        _ = CompiledExecutionEnvelopeProbe.Measure(
            host, plan, expected, CancellationToken.None, WarmupIterations);
        CompiledExecutionEnvelopeProbe.ForceGc();
        var busyPrimary = CompiledExecutionEnvelopeProbe.Measure(
            host, plan, expected, CancellationToken.None, Iterations);

        using var secondary = host.TryAcquireCompiledNoAuditState(
            plan,
            "main",
            executable,
            CompiledExecutionEnvelopeProbe.SuppressedOptions,
            CancellationToken.None,
            suppliedState: null,
            useAsyncWorker: false);
        _ = CompiledExecutionEnvelopeProbe.Measure(
            host, plan, expected, CancellationToken.None, WarmupIterations);
        CompiledExecutionEnvelopeProbe.ForceGc();
        var saturated = CompiledExecutionEnvelopeProbe.Measure(
            host, plan, expected, CancellationToken.None, Iterations);

        ValidateAllocations(busyPrimary, saturated, secondary.IsAcquired);
        Write(coldPrimary, activation, busyPrimary, saturated, secondary.IsAcquired);
    }

    private static async Task<CompiledExecutable> CreateEligibilityExecutableAsync(ExecutionPlan plan)
    {
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());
        var artifact = await compiler.CompileAsync(
            plan,
            new CompileOptions("main", Optimize: true),
            CancellationToken.None);
        return new CompiledExecutable(artifact, "Probe");
    }

    private static CompiledEnvelopeMeasurement MeasureColdPrimaryAdmissions(
        ExecutionPlan plan,
        int iterations)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var checksum = 0L;
        for (var i = 0; i < iterations; i++)
        {
            using var pool = new CompiledNoAuditRunStatePool();
            using var lease = pool.TryAcquire(plan);
            if (!lease.IsAcquired || lease.State is null)
            {
                throw new InvalidOperationException("cold primary admission did not acquire its state lane");
            }

            checksum++;
        }

        watch.Stop();
        return new CompiledEnvelopeMeasurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static void ValidateAllocations(
        CompiledEnvelopeMeasurement busyPrimary,
        CompiledEnvelopeMeasurement saturated,
        bool hasSecondary)
    {
        var busyBytes = busyPrimary.Bytes / (double)Iterations;
        var saturatedBytes = saturated.Bytes / (double)Iterations;
        if (!hasSecondary)
        {
            RequireFresh("busy-primary baseline", busyBytes);
            RequireFresh("single-lane saturation", saturatedBytes);
            return;
        }

        if (busyBytes > MaximumPooledBytesPerOperation)
        {
            throw new InvalidOperationException(
                $"expected a busy primary to reuse its secondary at <= " +
                $"{MaximumPooledBytesPerOperation:N1} B/op, got {busyBytes:N1} B/op");
        }

        RequireFresh("two-lane saturation", saturatedBytes);
        if (Math.Abs((saturatedBytes - busyBytes) - ExpectedStateSavingsBytesPerOperation) >
            AllocationNoiseBytesPerOperation)
        {
            throw new InvalidOperationException(
                $"expected the secondary to save {ExpectedStateSavingsBytesPerOperation:N1} B/op, " +
                $"got {saturatedBytes - busyBytes:N1} B/op");
        }
    }

    private static void RequireFresh(string name, double bytesPerOperation)
    {
        if (bytesPerOperation is < MinimumFreshBytesPerOperation or > MaximumFreshBytesPerOperation)
        {
            throw new InvalidOperationException(
                $"expected {name} to allocate {MinimumFreshBytesPerOperation:N1}-" +
                $"{MaximumFreshBytesPerOperation:N1} B/op, got {bytesPerOperation:N1} B/op");
        }
    }

    private static void Write(
        CompiledEnvelopeMeasurement coldPrimary,
        CompiledEnvelopeMeasurement activation,
        CompiledEnvelopeMeasurement busyPrimary,
        CompiledEnvelopeMeasurement saturated,
        bool hasSecondary)
    {
        Console.WriteLine("compiled overlapping execution-envelope control");
        Console.WriteLine("case                         total ms    allocated B       B/op   checksum");
        Console.WriteLine(
            $"cold primary admission         {coldPrimary.ElapsedMilliseconds,8:N1} {coldPrimary.Bytes,14:N0} " +
            $"{coldPrimary.Bytes / (double)ColdPrimaryIterations,10:N1} {coldPrimary.Checksum,10:N0}");
        Console.WriteLine(
            $"secondary activation          {activation.ElapsedMilliseconds,8:N1} {activation.Bytes,14:N0} " +
            $"{activation.Bytes,10:N1} {activation.Checksum,10:N0}");
        WriteRow("busy primary", busyPrimary);
        WriteRow("all retained lanes busy", saturated);
        Console.WriteLine($"retained lanes = {(hasSecondary ? 2 : 1):N0}");
    }

    private static void WriteRow(string name, CompiledEnvelopeMeasurement measurement)
        => Console.WriteLine(
            $"{name,-29} {measurement.ElapsedMilliseconds,8:N1} {measurement.Bytes,14:N0} " +
            $"{measurement.Bytes / (double)Iterations,10:N1} {measurement.Checksum,10:N0}");
}
