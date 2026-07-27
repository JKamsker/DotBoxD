using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class SandboxHostCompiledNoAuditStateBypassAllocationTests
{
    private const int WarmupIterations = 5_000;
    private const int MeasuredIterations = 100_000;
    private const double MinimumFreshBytesPerRun = 500;
    private const double MaximumFreshBytesPerRun = 520;
    private const int PendingWarmupIterations = 20;
    private const int PendingMeasuredIterations = 100;
    private const long ExpectedPendingBytesPerSuspension = 1_032;

    private static readonly SandboxExecutionOptions PooledOptions = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    private static readonly SandboxExecutionOptions AuditedOptions = PooledOptions with
    {
        SuppressSuccessfulRunSummaryAudit = false
    };

    [Fact]
    public async Task Cancelable_tokens_keep_the_fresh_context_path()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PrepareAsync(host);
        using var cancellation = new CancellationTokenSource();
        _ = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, PooledOptions, cancellation.Token);
        _ = MeasureFresh(host, plan, cancellation.Token, WarmupIterations);
        ForceGc();

        var measured = MeasureFresh(host, plan, cancellation.Token, MeasuredIterations);
        var bytesPerRun = measured.AllocatedBytes / (double)MeasuredIterations;

        Console.WriteLine($"cancelable compiled no-audit bypass: {bytesPerRun:N3} B/run.");
        Assert.InRange(bytesPerRun, MinimumFreshBytesPerRun, MaximumFreshBytesPerRun);
        Assert.Equal(checked(7L * MeasuredIterations), measured.Checksum);
    }

    [Fact]
    public async Task Pending_audited_provider_lookup_keeps_its_pre_pool_suspension_size()
    {
        var compiler = new ControlledPendingCompiler();
        using var host = SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(compiler);
        });
        var plan = await PrepareAsync(host);
        var inner = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());
        compiler.Artifact = await inner.CompileAsync(
            plan,
            new CompileOptions("main"),
            CancellationToken.None);

        for (var i = 0; i < PendingWarmupIterations; i++)
        {
            _ = await StartMeasureAndCompleteAsync(host, plan, compiler);
        }

        ForceGc();
        var observed = new long[PendingMeasuredIterations];
        for (var i = 0; i < observed.Length; i++)
        {
            observed[i] = await StartMeasureAndCompleteAsync(host, plan, compiler);
        }

        Console.WriteLine(
            $"pending audited provider dispatch: {ExpectedPendingBytesPerSuspension:N0} B/suspension " +
            $"(range {observed.Min():N0}-{observed.Max():N0}).");
        for (var i = 1; i < observed.Length; i++)
        {
            Assert.Equal(ExpectedPendingBytesPerSuspension, observed[i]);
        }

        Assert.False(host.HasCompiledNoAuditRunStatePool);
    }

    private static async Task<long> StartMeasureAndCompleteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        ControlledPendingCompiler compiler)
    {
        compiler.Arm();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var pending = host.ExecuteAsync(plan, "main", SandboxValue.Unit, AuditedOptions);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(pending.IsCompleted);
        compiler.Complete();
        var result = await pending;
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, Assert.IsType<I32Value>(result.Value).Value);
        Assert.NotEmpty(result.AuditEvents);
        return allocated;
    }

    private static AllocationSummary MeasureFresh(
        SandboxHost host,
        ExecutionPlan plan,
        CancellationToken cancellationToken,
        int iterations)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0L;
        for (var i = 0; i < iterations; i++)
        {
            var pending = host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                PooledOptions,
                cancellationToken);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Cancelable compiled execution unexpectedly became asynchronous.");
            }

            var result = pending.Result;
            if (result is not
                {
                    Succeeded: true,
                    Error: null,
                    Value: I32Value { Value: 7 },
                    ActualMode: ExecutionMode.Compiled,
                    ExecutionDispatched: true,
                    AuditEvents.Count: 0
                })
            {
                throw new InvalidOperationException("Cancelable compiled execution changed its result.");
            }

            checksum += 7;
        }

        return new AllocationSummary(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static async Task<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(ModuleJson);
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(long.MaxValue)
                .WithWallTime(TimeSpan.FromMinutes(5))
                .Build());
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class ControlledPendingCompiler : ISandboxCompiler
    {
        private TaskCompletionSource<CompiledArtifact>? _completion;

        public CompiledArtifact Artifact { get; set; } = null!;

        public void Arm()
            => _completion = new TaskCompletionSource<CompiledArtifact>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete()
            => _completion!.SetResult(Artifact);

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => new(_completion!.Task);
    }

    private const string ModuleJson = """
    {
      "id": "compiled-no-audit-state-pool-bypass",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "i32": 7 } }]
      }]
    }
    """;

    private readonly record struct AllocationSummary(long AllocatedBytes, long Checksum);
}
