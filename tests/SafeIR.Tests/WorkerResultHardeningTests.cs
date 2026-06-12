using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class WorkerResultHardeningTests
{
    [Fact]
    public async Task Require_deterministic_denies_before_worker_invocation()
    {
        var worker = new TestWorker();
        var host = Host(worker);
        var plan = await PrepareAsync(host, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions
            {
                Isolation = SandboxIsolation.WorkerProcess,
                RequireDeterministic = true
            });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Equal(0, worker.Calls);
    }

    [Fact]
    public async Task Worker_process_execution_has_host_side_wall_time_watchdog()
    {
        var worker = new TestWorker { Delay = TimeSpan.FromSeconds(5) };
        var host = Host(worker);
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithWallTime(TimeSpan.FromMilliseconds(20))
            .Build();
        var plan = await PrepareAsync(host, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        Assert.Equal(1, worker.Calls);
    }

    [Fact]
    public async Task Worker_result_with_underreported_resource_usage_is_rejected()
    {
        var worker = new TestWorker { UnderreportMaxFuel = true };
        var host = Host(worker);
        var plan = await PrepareAsync(host, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Worker_result_with_malformed_run_summary_is_rejected()
    {
        var worker = new TestWorker { OmitSummaryFields = true };
        var host = Host(worker);
        var plan = await PrepareAsync(host, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Compiled_worker_success_requires_runtime_envelope_fields()
    {
        var worker = new TestWorker
        {
            ResultMode = ExecutionMode.Compiled,
            ArtifactHash = new string('c', 64),
            OmitCompiledEnvelopeFields = true
        };
        var host = Host(worker);
        var plan = await PrepareAsync(host, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
    }

    private static SandboxHost Host(TestWorker worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host, SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, policy);
    }

    private static SandboxValue Input()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

    private sealed class TestWorker : ISandboxWorkerClient
    {
        public int Calls { get; private set; }
        public TimeSpan Delay { get; init; }
        public bool UnderreportMaxFuel { get; init; }
        public bool OmitSummaryFields { get; init; }
        public bool OmitCompiledEnvelopeFields { get; init; }
        public ExecutionMode ResultMode { get; init; } = ExecutionMode.Interpreted;
        public string? ArtifactHash { get; init; }

        public async ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            var budget = new ResourceMeter(plan.Budget);
            var usage = budget.Snapshot();
            if (UnderreportMaxFuel)
            {
                usage = usage with { MaxFuel = usage.MaxFuel - 1 };
            }

            var runId = options.RunId ?? SandboxRunId.New();
            var audit = new InMemoryAuditSink();
            var fields = OmitSummaryFields
                ? null
                : RunSummaryAuditFields.Create(
                    plan,
                    budget,
                    ResultMode,
                    "None",
                    ResultMode == ExecutionMode.Compiled && !OmitCompiledEnvelopeFields ? "LoadedAssembly" : null,
                    ResultMode == ExecutionMode.Compiled && !OmitCompiledEnvelopeFields ? "worker-cache-key" : null,
                    ResultMode == ExecutionMode.Compiled && !OmitCompiledEnvelopeFields ? ArtifactHash : null);
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: fields));
            return new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.FromInt32(35),
                ResourceUsage = usage,
                AuditEvents = audit.Events,
                ActualMode = ResultMode,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash,
                ArtifactHash = ArtifactHash
            };
        }
    }
}
