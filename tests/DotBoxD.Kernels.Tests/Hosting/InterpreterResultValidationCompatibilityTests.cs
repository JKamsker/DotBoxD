using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Hosting;

public sealed class InterpreterResultValidationCompatibilityTests
{
    private const string RejectedAuditMarker = "rejected custom interpreter evidence";

    [Fact]
    public async Task Delegating_interpreter_preserves_coherent_quota_failure_usage()
    {
        var result = await ExecuteAsync(relabelQuotaFailure: false);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.True(result.ResourceUsage.FuelUsed > result.ResourceUsage.MaxFuel);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.Equal(
            result.ResourceUsage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            summary.Fields!["fuelUsed"]);
    }

    [Fact]
    public async Task Delegating_interpreter_cannot_relabel_over_budget_usage_as_another_failure()
    {
        var result = await ExecuteAsync(relabelQuotaFailure: true);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal("interpreter execution failed", result.Error.SafeMessage);
        Assert.Equal(0, result.ResourceUsage.FuelUsed);
        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.Equal("0", summary.Fields!["fuelUsed"]);
    }

    [Fact]
    public async Task Binding_failure_discards_rejected_audit_evidence_before_publication()
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter(new RejectedAuditInterpreter());
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var module = await host.ImportJsonAsync(
            SandboxTestHost.PureScoreJson("interpreter-rejected-audit"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                RunId = SandboxRunId.New()
            });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Equal("binding audit evidence was rejected", result.Error.SafeMessage);
        var summary = Assert.Single(result.AuditEvents);
        Assert.Equal("RunSummary", summary.Kind);
        Assert.Equal(1, summary.SequenceNumber);
        Assert.Equal(SandboxErrorCode.BindingFailure, summary.ErrorCode);
        Assert.Equal(result.AuditEvents, observed);
        Assert.DoesNotContain(observed, auditEvent => auditEvent.Message == RejectedAuditMarker);
    }

    [Fact]
    public async Task Successful_result_with_failed_binding_evidence_is_rejected()
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter(new FailedBindingEvidenceInterpreter());
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var module = await host.ImportJsonAsync(LogJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().GrantLogging().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal("interpreter execution failed", result.Error.SafeMessage);
        Assert.Single(result.AuditEvents);
        Assert.Equal("RunSummary", result.AuditEvents[0].Kind);
        Assert.Equal(result.AuditEvents, observed);
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteAsync(bool relabelQuotaFailure)
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter(new DelegatingInterpreter(relabelQuotaFailure));
        });
        var module = await host.ImportJsonAsync(
            SandboxTestHost.PureScoreJson("delegating-interpreter-quota"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(0).Build());

        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
    }

    private sealed class DelegatingInterpreter(bool relabelQuotaFailure) : ISandboxInterpreter
    {
        private readonly SandboxInterpreter _inner = new();

        public async ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken)
        {
            var result = await _inner.ExecuteAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
            if (!relabelQuotaFailure || result.Error?.Code != SandboxErrorCode.QuotaExceeded)
            {
                return result;
            }

            var error = new SandboxError(SandboxErrorCode.HostFailure, "custom host failure");
            var audit = result.AuditEvents.Select(auditEvent => auditEvent.Kind == "RunSummary"
                ? auditEvent with { ErrorCode = error.Code, Message = error.SafeMessage }
                : auditEvent).ToArray();
            return result with { Error = error, AuditEvents = audit };
        }
    }

    private sealed class RejectedAuditInterpreter : ISandboxInterpreter
    {
        public ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken)
        {
            var runId = options.RunId ?? SandboxRunId.New();
            var budget = new ResourceMeter(plan.Budget);
            var error = new SandboxError(
                SandboxErrorCode.BindingFailure,
                "binding audit evidence was rejected");
            var audit = new InMemoryAuditSink();
            audit.Write(new SandboxAuditEvent(
                SandboxRunId.New(),
                "RejectedEvidence",
                DateTimeOffset.UtcNow,
                true,
                Message: RejectedAuditMarker));
            audit.Write(new SandboxAuditEvent(
                runId,
                "RunSummary",
                DateTimeOffset.UtcNow,
                false,
                ResourceId: $"module:{plan.ModuleHash}",
                ErrorCode: error.Code,
                Fields: RunSummaryAuditFields.Create(
                    plan,
                    budget,
                    ExecutionMode.Interpreted,
                    "None")));
            return ValueTask.FromResult(new SandboxExecutionResult
            {
                Succeeded = false,
                Error = error,
                ResourceUsage = budget.Snapshot(),
                AuditEvents = audit.Events,
                ActualMode = ExecutionMode.Interpreted,
                ExecutionDispatched = true,
                ModuleHash = plan.ModuleHash,
                PlanHash = plan.PlanHash,
                PolicyHash = plan.PolicyHash
            });
        }
    }

    private sealed class FailedBindingEvidenceInterpreter : ISandboxInterpreter
    {
        private readonly SandboxInterpreter _inner = new();

        public async ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken)
        {
            var result = await _inner.ExecuteAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
            var audit = result.AuditEvents.Select(auditEvent => auditEvent.Kind == BindingAuditKinds.SandboxLog
                ? FailedBindingAudit(auditEvent)
                : auditEvent).ToArray();
            return result with { AuditEvents = audit };
        }

        private static SandboxAuditEvent FailedBindingAudit(SandboxAuditEvent auditEvent)
            => new(
                auditEvent.RunId,
                auditEvent.Kind,
                auditEvent.Timestamp,
                false,
                auditEvent.BindingId,
                auditEvent.CapabilityId,
                auditEvent.Effect,
                auditEvent.ResourceId,
                SandboxErrorCode.BindingFailure,
                auditEvent.Message,
                auditEvent.Bytes,
                auditEvent.Fields,
                auditEvent.SequenceNumber);
    }

    private static string LogJson()
        => """
        {
          "id": "interpreter-failed-binding-evidence",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "test logs" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "ok" }] } }
              ]
            }
          ]
        }
        """;
}
