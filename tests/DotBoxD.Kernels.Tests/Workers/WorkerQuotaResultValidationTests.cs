using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests.Workers.WorkerAuditValidationTestSupport;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerQuotaResultValidationTests
{
    [Fact]
    public async Task Coherent_quota_failure_preserves_attempted_over_limit_usage()
    {
        using var host = Host(new QuotaFailureWorker());
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.True(result.ResourceUsage.FuelUsed > result.ResourceUsage.MaxFuel);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "WorkerIsolationFailed");
        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.Equal(SandboxErrorCode.QuotaExceeded, summary.ErrorCode);
        Assert.Equal(
            result.ResourceUsage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            summary.Fields!["fuelUsed"]);
    }

    [Fact]
    public async Task Quota_failure_with_multiple_over_limit_counters_is_rejected()
    {
        using var host = Host(new QuotaFailureWorker(reportSecondOverage: true));
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal("worker resource usage was malformed", result.Error.SafeMessage);
        Assert.Contains(result.AuditEvents, auditEvent => auditEvent.Kind == "WorkerIsolationFailed");
    }

    private sealed class QuotaFailureWorker(bool reportSecondOverage = false) : ISandboxWorkerClient
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
            var usage = new ResourceMeter(plan.Budget).Snapshot() with
            {
                FuelUsed = plan.Budget.MaxFuel + 1,
                LoopIterations = reportSecondOverage ? plan.Budget.MaxLoopIterations + 1 : 0
            };
            var fields = new Dictionary<string, string>(
                RunSummaryAuditFields.Create(
                    plan,
                    new ResourceMeter(plan.Budget),
                    ExecutionMode.Interpreted,
                    "None"),
                StringComparer.Ordinal)
            {
                ["fuelUsed"] = usage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["loopIterations"] = usage.LoopIterations.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            var error = new SandboxError(SandboxErrorCode.QuotaExceeded, "fuel exhausted");
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                false,
                ResourceId: $"module:{plan.ModuleHash}",
                ErrorCode: error.Code,
                Fields: fields));

            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = false,
                Error = error,
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
