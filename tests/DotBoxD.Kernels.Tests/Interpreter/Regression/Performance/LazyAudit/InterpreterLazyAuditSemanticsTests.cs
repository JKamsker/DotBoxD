using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LazyAudit;

public sealed class InterpreterLazyAuditSemanticsTests
{
    [Fact]
    public async Task Suppressed_pure_success_has_an_empty_audit_snapshot()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InterpreterLazyAuditTestSupport.PreparePureAsync(host);

        var result = await ExecuteAsync(
            plan,
            SandboxValue.FromInt32(17),
            InterpreterLazyAuditTestSupport.SuppressedOptions());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(17, ((I32Value)result.Value!).Value);
        Assert.Empty(result.AuditEvents);
    }

    [Fact]
    public async Task Suppressed_failure_materializes_one_summary_with_explicit_run_id_and_start_time()
    {
        var logicalNow = new DateTimeOffset(2042, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var policy = SandboxPolicyBuilder.Create().WithFuel(100).Deterministic(logicalNow, randomSeed: 1).Build();
        using var host = SandboxTestHost.Create();
        var plan = await InterpreterLazyAuditTestSupport.PreparePureAsync(host, policy);
        var runId = SandboxRunId.New();

        var result = await ExecuteAsync(
            plan,
            SandboxValue.Unit,
            InterpreterLazyAuditTestSupport.SuppressedOptions(runId));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        var summary = Assert.Single(result.AuditEvents);
        Assert.Equal("RunSummary", summary.Kind);
        Assert.False(summary.Success);
        Assert.Equal(runId, summary.RunId);
        Assert.Equal(logicalNow, summary.Timestamp);
        Assert.Equal(result.Error.Code, summary.ErrorCode);
    }

    [Fact]
    public async Task Debug_trace_materializes_audit_and_preserves_the_explicit_run_id()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InterpreterLazyAuditTestSupport.PreparePureAsync(host);
        var runId = SandboxRunId.New();

        var result = await ExecuteAsync(
            plan,
            SandboxValue.FromInt32(17),
            InterpreterLazyAuditTestSupport.SuppressedOptions(runId, enableDebugTrace: true));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Contains(result.AuditEvents, audit => audit.Kind == "DebugTrace");
        Assert.All(result.AuditEvents, audit => Assert.Equal(runId, audit.RunId));
        Assert.DoesNotContain(result.AuditEvents, audit => audit.Kind == "RunSummary");
    }

    [Fact]
    public async Task Referenced_binding_audit_is_preserved_when_success_summary_is_suppressed()
    {
        var logicalNow = new DateTimeOffset(2043, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var policy = SandboxPolicyBuilder.Create().GrantLogging().Deterministic(logicalNow, randomSeed: 1).Build();
        using var host = SandboxTestHost.Create();
        var plan = await InterpreterLazyAuditTestSupport.PrepareLogAsync(host, policy);
        var runId = SandboxRunId.New();

        var result = await ExecuteAsync(
            plan,
            SandboxValue.Unit,
            InterpreterLazyAuditTestSupport.SuppressedOptions(runId));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var log = Assert.Single(result.AuditEvents);
        Assert.Equal(BindingAuditKinds.SandboxLog, log.Kind);
        Assert.Equal("log.info", log.BindingId);
        Assert.Equal(runId, log.RunId);
        Assert.Equal(logicalNow, log.Timestamp);
    }

    [Fact]
    public async Task Forged_empty_binding_references_preserve_synthesized_binding_failure_and_summary()
    {
        var logicalNow = new DateTimeOffset(2044, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var policy = SandboxPolicyBuilder.Create().GrantLogging().Deterministic(logicalNow, randomSeed: 1).Build();
        using var host = SandboxTestHost.Create();
        var prepared = await InterpreterLazyAuditTestSupport.PrepareLogAsync(host, policy);
        var forged = InterpreterLazyAuditTestSupport.WithBindingReferences(
            prepared,
            InterpreterLazyAuditTestSupport.References());

        var result = await ExecuteAsync(
            forged,
            SandboxValue.Unit,
            InterpreterLazyAuditTestSupport.SuppressedOptions());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Collection(
            result.AuditEvents,
            binding =>
            {
                Assert.Equal(BindingAuditKinds.BindingCall, binding.Kind);
                Assert.Equal("log.info", binding.BindingId);
                Assert.False(binding.Success);
                Assert.Equal(SandboxErrorCode.ValidationError, binding.ErrorCode);
                Assert.Equal(logicalNow, binding.Timestamp);
            },
            summary =>
            {
                Assert.Equal("RunSummary", summary.Kind);
                Assert.False(summary.Success);
                Assert.Equal(SandboxErrorCode.ValidationError, summary.ErrorCode);
                Assert.Equal(logicalNow, summary.Timestamp);
            });
        Assert.Equal(result.AuditEvents[0].RunId, result.AuditEvents[1].RunId);
        Assert.NotEqual(Guid.Empty, result.AuditEvents[0].RunId.Value);
        Assert.Equal(1, result.AuditEvents[0].SequenceNumber);
        Assert.Equal(2, result.AuditEvents[1].SequenceNumber);
    }

    [Fact]
    public async Task Missing_binding_reference_metadata_uses_full_audit_path()
    {
        var policy = SandboxPolicyBuilder.Create().GrantLogging().Build();
        using var host = SandboxTestHost.Create();
        var prepared = await InterpreterLazyAuditTestSupport.PrepareLogAsync(host, policy);
        var missing = InterpreterLazyAuditTestSupport.WithBindingReferences(
            prepared,
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal));
        var runId = SandboxRunId.New();

        var result = await ExecuteAsync(
            missing,
            SandboxValue.Unit,
            InterpreterLazyAuditTestSupport.SuppressedOptions(runId));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var binding = Assert.Single(result.AuditEvents);
        Assert.Equal(BindingAuditKinds.SandboxLog, binding.Kind);
        Assert.Equal("log.info", binding.BindingId);
        Assert.Equal(runId, binding.RunId);
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options)
        => new SandboxInterpreter().ExecuteAsync(plan, "main", input, options, CancellationToken.None);
}
