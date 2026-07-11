using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerFailedBindingAuditValidationTests
{
    [Fact]
    public async Task Worker_rejects_success_result_that_carries_failed_binding_audit()
    {
        var worker = new FailedLogAuditWorker();
        using var host = WorkerAuditValidationTestSupport.LogHost(worker);
        var module = await host.ImportJsonAsync(WorkerAuditValidationTestSupport.LogJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantLogging()
                .WithFuel(1_000)
                .WithMaxLogEvents(10)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Contains(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    [Fact]
    public async Task Worker_accepts_successful_fallback_result_that_carries_verifier_failure_audit()
    {
        var worker = new VerifierFailureFallbackWorker();
        using var host = WorkerAuditValidationTestSupport.Host(worker);
        var plan = await WorkerAuditValidationTestSupport.PrepareAsync(host);

        var result = await WorkerAuditValidationTestSupport.ExecuteAsync(host, plan);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "VerifierFailure" &&
            !e.Success &&
            e.ErrorCode == SandboxErrorCode.VerifierFailure);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "ExecutionFallback" &&
            !e.Success &&
            e.ErrorCode == SandboxErrorCode.VerifierFailure);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxResourceUsage Usage(ExecutionPlan plan)
        => new(
            FuelUsed: 2,
            MaxFuel: plan.Budget.MaxFuel,
            LoopIterations: 0,
            AllocatedBytes: 0,
            HostCalls: 1,
            FileBytesRead: 0,
            FileBytesWritten: 0,
            NetworkBytesRead: 0,
            NetworkBytesWritten: 0,
            LogEvents: 1,
            CollectionElements: 0,
            StringBytes: 0);

    private static Dictionary<string, string> SummaryFields(ExecutionPlan plan, SandboxResourceUsage usage)
    {
        var fields = new Dictionary<string, string>(
            RunSummaryAuditFields.Create(plan, new ResourceMeter(plan.Budget), ExecutionMode.Interpreted, "None"),
            StringComparer.Ordinal);
        fields["fuelUsed"] = usage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields["hostCalls"] = usage.HostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields["logEvents"] = usage.LogEvents.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return fields;
    }

    private sealed class FailedLogAuditWorker : ISandboxWorkerClient
    {
        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runId = options.RunId ?? SandboxRunId.New();
            var usage = Usage(plan);
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: SummaryFields(plan, usage)));
            audit.Write(new SandboxAuditEvent(
                runId,
                "SandboxLog",
                DateTimeOffset.UtcNow,
                false,
                BindingId: "log.info",
                CapabilityId: "log.write",
                Effect: SandboxEffect.Audit,
                ResourceId: "log:info",
                ErrorCode: SandboxErrorCode.PermissionDenied,
                Message: "permission denied",
                Fields: WorkerAuditValidationTestSupport.BindingFields(plan, "log")));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.Unit,
                ResourceUsage = usage,
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }
    }

    private sealed class VerifierFailureFallbackWorker : ISandboxWorkerClient
    {
        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runId = options.RunId ?? SandboxRunId.New();
            var budget = new ResourceMeter(plan.Budget);
            var usage = budget.Snapshot();
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                true,
                ResourceId: $"module:{plan.ModuleHash}",
                Fields: new Dictionary<string, string>(
                    RunSummaryAuditFields.Create(plan, budget, ExecutionMode.Interpreted, "None"),
                    StringComparer.Ordinal)));
            audit.Write(new SandboxAuditEvent(
                runId,
                "VerifierFailure",
                DateTimeOffset.UtcNow,
                false,
                ResourceId: $"module:{plan.ModuleHash}",
                ErrorCode: SandboxErrorCode.VerifierFailure,
                Message: "compiled artifact failed verification"));
            audit.Write(new SandboxAuditEvent(
                runId,
                "ExecutionFallback",
                DateTimeOffset.UtcNow,
                false,
                ResourceId: $"module:{plan.ModuleHash}",
                ErrorCode: SandboxErrorCode.VerifierFailure,
                Message: "compiled artifact failed verification; fell back to interpreted mode"));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = true,
                Value = SandboxValue.FromInt32(35),
                ResourceUsage = usage,
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }
    }
}
