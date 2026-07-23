using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Hosting;

public sealed class TrustedInterpreterBoundarySemanticsTests
{
    [Fact]
    public async Task Unsuppressed_success_preserves_audit_and_explicit_run_id()
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = TrustedInterpreterBoundaryTestSupport.CreateBuiltInHost(observed.Add);
        var logicalNow = new DateTimeOffset(2045, 6, 7, 8, 9, 10, TimeSpan.Zero);
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000).Deterministic(logicalNow, 1).Build();
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule,
            policy);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, RunId = runId });

        AssertPureSuccess(result, plan);
        var summary = Assert.Single(result.AuditEvents);
        Assert.Equal("RunSummary", summary.Kind);
        Assert.Equal(runId, summary.RunId);
        Assert.Equal(logicalNow, summary.Timestamp);
        Assert.True(summary.Success);
        Assert.Equal("Interpreted", summary.Fields!["executionMode"]);
        Assert.Equal("True", summary.Fields["executionDispatched"]);
        Assert.Equal(result.AuditEvents, observed);
    }

    [Fact]
    public async Task Suppressed_success_with_explicit_run_id_remains_audit_free()
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = TrustedInterpreterBoundaryTestSupport.CreateBuiltInHost(observed.Add);
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            TrustedInterpreterBoundaryTestSupport.SuppressedOptions(runId: SandboxRunId.New()));

        AssertPureSuccess(result, plan);
        Assert.Empty(result.AuditEvents);
        Assert.Empty(observed);
    }

    [Fact]
    public async Task Binding_bearing_success_preserves_audit_when_summary_is_suppressed()
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = TrustedInterpreterBoundaryTestSupport.CreateBuiltInHost(
            observed.Add,
            addLogBindings: true);
        var logicalNow = new DateTimeOffset(2046, 7, 8, 9, 10, 11, TimeSpan.Zero);
        var policy = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(1_000)
            .Deterministic(logicalNow, 1)
            .Build();
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.LoggedSuccessModule,
            policy);
        var runId = SandboxRunId.New();
        Assert.True(plan.BindingReferences.TryGetValue("main", out var references));
        Assert.Contains("log.info", references);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            TrustedInterpreterBoundaryTestSupport.SuppressedOptions(runId: runId));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Same(SandboxValue.Unit, result.Value);
        TrustedInterpreterBoundaryTestSupport.AssertEnvelope(result, plan);
        var log = Assert.Single(result.AuditEvents);
        Assert.Equal(BindingAuditKinds.SandboxLog, log.Kind);
        Assert.Equal("log.info", log.BindingId);
        Assert.Equal(runId, log.RunId);
        Assert.Equal(logicalNow, log.Timestamp);
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(1, result.ResourceUsage.LogEvents);
        Assert.Equal(result.AuditEvents, observed);
    }

    [Fact]
    public async Task Debug_traced_success_preserves_trace_when_summary_is_suppressed()
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = TrustedInterpreterBoundaryTestSupport.CreateBuiltInHost(observed.Add);
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            TrustedInterpreterBoundaryTestSupport.SuppressedOptions(
                runId: runId,
                enableDebugTrace: true));

        AssertPureSuccess(result, plan);
        Assert.NotEmpty(result.AuditEvents);
        Assert.All(result.AuditEvents, auditEvent =>
        {
            Assert.Equal("DebugTrace", auditEvent.Kind);
            Assert.Equal(runId, auditEvent.RunId);
        });
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.Equal(result.AuditEvents, observed);
    }

    [Fact]
    public async Task Built_in_failure_preserves_summary_and_explicit_run_id()
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = TrustedInterpreterBoundaryTestSupport.CreateBuiltInHost(observed.Add);
        var logicalNow = new DateTimeOffset(2047, 8, 9, 10, 11, 12, TimeSpan.Zero);
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000).Deterministic(logicalNow, 1).Build();
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.PureFailureModule,
            policy);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            TrustedInterpreterBoundaryTestSupport.SuppressedOptions(runId: runId));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal("integer division by zero", result.Error.SafeMessage);
        TrustedInterpreterBoundaryTestSupport.AssertEnvelope(result, plan);
        var summary = Assert.Single(result.AuditEvents);
        Assert.Equal("RunSummary", summary.Kind);
        Assert.Equal(runId, summary.RunId);
        Assert.Equal(logicalNow, summary.Timestamp);
        Assert.False(summary.Success);
        Assert.Equal(result.Error.Code, summary.ErrorCode);
        Assert.Equal(result.AuditEvents, observed);
    }

    [Fact]
    public async Task Auto_interpreted_suppressed_success_retains_full_host_semantics()
    {
        using var host = TrustedInterpreterBoundaryTestSupport.CreateBuiltInHost();
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            TrustedInterpreterBoundaryTestSupport.SuppressedOptions(ExecutionMode.Auto));

        AssertPureSuccess(result, plan);
        Assert.Empty(result.AuditEvents);
    }

    [Fact]
    public async Task Pre_dispatch_cancellation_still_uses_the_explicit_run_id()
    {
        using var host = TrustedInterpreterBoundaryTestSupport.CreateBuiltInHost();
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule);
        var runId = SandboxRunId.New();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            TrustedInterpreterBoundaryTestSupport.SuppressedOptions(runId: runId),
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
        Assert.Equal(plan.ModuleHash, result.ModuleHash);
        Assert.Equal(plan.PlanHash, result.PlanHash);
        Assert.Equal(plan.PolicyHash, result.PolicyHash);
        Assert.Equal(0, result.ResourceUsage.FuelUsed);
        var summary = Assert.Single(result.AuditEvents);
        Assert.Equal(runId, summary.RunId);
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, summary.ErrorCode);
        Assert.Equal("False", summary.Fields!["executionDispatched"]);
    }

    private static void AssertPureSuccess(SandboxExecutionResult result, ExecutionPlan plan)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, Assert.IsType<I32Value>(result.Value).Value);
        Assert.Null(result.Error);
        TrustedInterpreterBoundaryTestSupport.AssertEnvelope(result, plan);
        Assert.Equal(3, result.ResourceUsage.FuelUsed);
        Assert.Equal(0, result.ResourceUsage.LoopIterations);
        Assert.Equal(0, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
        Assert.Equal(0, result.ResourceUsage.FileBytesRead);
        Assert.Equal(0, result.ResourceUsage.FileBytesWritten);
        Assert.Equal(0, result.ResourceUsage.NetworkBytesRead);
        Assert.Equal(0, result.ResourceUsage.NetworkBytesWritten);
        Assert.Equal(0, result.ResourceUsage.LogEvents);
        Assert.Equal(0, result.ResourceUsage.CollectionElements);
        Assert.Equal(0, result.ResourceUsage.StringBytes);
    }
}
