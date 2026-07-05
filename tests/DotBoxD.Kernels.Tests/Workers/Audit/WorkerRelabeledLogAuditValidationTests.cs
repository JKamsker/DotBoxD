using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerRelabeledLogAuditValidationTests
{
    [Theory]
    [InlineData("BindingCall")]
    [InlineData("PluginMessage")]
    public async Task Worker_rejects_log_audit_relabelled_as_non_log_binding_event(string auditKind)
    {
        var worker = new ForgedRelabeledLogWorker(auditKind);
        using var host = WorkerAuditValidationTestSupport.LogHost(worker);
        var module = await host.ImportJsonAsync(WorkerAuditValidationTestSupport.LogJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantLogging()
                .WithFuel(1_000)
                .WithMaxLogEvents(10)
                .WithMaxLogMessageLength(4)
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

    private sealed class ForgedRelabeledLogWorker(string auditKind) : ISandboxWorkerClient
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
            var usage = new SandboxResourceUsage(
                FuelUsed: plan.Budget.MaxFuel,
                MaxFuel: plan.Budget.MaxFuel,
                LoopIterations: 0,
                AllocatedBytes: 0,
                HostCalls: 1,
                FileBytesRead: 0,
                FileBytesWritten: 0,
                NetworkBytesRead: 0,
                NetworkBytesWritten: 0,
                LogEvents: 0,
                CollectionElements: 0,
                StringBytes: 0);

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
                auditKind,
                DateTimeOffset.UtcNow,
                true,
                BindingId: "log.info",
                CapabilityId: "log.write",
                Effect: SandboxEffect.Audit,
                ResourceId: "log:info",
                Message: "too-long",
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

        private static Dictionary<string, string> SummaryFields(
            ExecutionPlan plan,
            SandboxResourceUsage usage)
        {
            var fields = new Dictionary<string, string>(
                RunSummaryAuditFields.Create(
                    plan,
                    new ResourceMeter(plan.Budget),
                    ExecutionMode.Interpreted,
                    "None"),
                StringComparer.Ordinal);
            fields["fuelUsed"] = usage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields["hostCalls"] = usage.HostCalls.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields["logEvents"] = usage.LogEvents.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return fields;
        }
    }
}
