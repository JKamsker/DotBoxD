using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class BindingAuditInvocationCompatibilityTests
{
    private const string BindingId = "test.async.audit";

    [Fact]
    public async Task Missing_success_seal_suppresses_late_success_before_and_after_failure_fallback()
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        var checkpoint = context.AuditCheckpoint();
        using var invocation = context.BeginBindingAuditInvocation(descriptor, checkpoint);
        var releaseBeforeFailure = NewSignal();
        var releaseAfterFailure = NewSignal();
        var beforeFailure = WriteSuccessAfterAsync(context, releaseBeforeFailure.Task, "late before fallback");
        var afterFailure = WriteSuccessAfterAsync(context, releaseAfterFailure.Task, "late after fallback");

        var exception = Assert.Throws<SandboxRuntimeException>(
            () => context.EnsureRequiredBindingSuccessAudit(descriptor, invocation));
        Assert.Equal(SandboxErrorCode.BindingFailure, exception.Error.Code);

        releaseBeforeFailure.TrySetResult();
        await beforeFailure.WaitAsync(TimeSpan.FromSeconds(2));
        context.EnsureRequiredBindingFailureAudit(
            descriptor,
            invocation,
            SandboxErrorCode.BindingFailure);
        releaseAfterFailure.TrySetResult();
        await afterFailure.WaitAsync(TimeSpan.FromSeconds(2));

        var fallback = Assert.Single(audit.Events);
        Assert.False(fallback.Success);
        Assert.Equal(SandboxErrorCode.BindingFailure, fallback.ErrorCode);
        Assert.Equal("binding failed before emitting audit", fallback.Message);
    }

    [Fact]
    public void Invocation_checkpoint_does_not_accept_matching_historical_audit()
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        WriteAudit(context, success: false, SandboxErrorCode.Timeout, "historical");
        var checkpoint = context.AuditCheckpoint();

        using var invocation = context.BeginBindingAuditInvocation(descriptor, checkpoint);
        context.EnsureRequiredBindingFailureAudit(descriptor, invocation, SandboxErrorCode.Timeout);

        Assert.Equal(2, audit.Events.Count);
        Assert.Equal("historical", audit.Events[0].Message);
        Assert.Equal("binding failed before emitting audit", audit.Events[1].Message);
        Assert.Equal(checkpoint + 1, audit.EventsWritten);
    }

    [Fact]
    public void Custom_sink_keeps_public_sink_and_checkpoint_behavior()
    {
        var descriptor = Descriptor();
        var audit = new ForwardingAuditSink();
        var context = CreateContext(descriptor, audit);
        var checkpoint = context.AuditCheckpoint();

        using var invocation = context.BeginBindingAuditInvocation(descriptor, checkpoint);
        Assert.Same(audit, context.Audit);
        WriteAudit(context, success: true, null, "custom success");
        context.EnsureRequiredBindingSuccessAudit(descriptor, invocation);

        Assert.Equal(checkpoint + 1, context.AuditCheckpoint());
        var success = Assert.Single(audit.Events);
        Assert.True(success.Success);
        Assert.Equal("custom success", success.Message);
    }

    [Fact]
    public void In_memory_fallback_uses_custom_descriptor_audit_kind()
    {
        var descriptor = Descriptor() with { AuditKind = BindingAuditKinds.SandboxLog };
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        using var invocation = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint());

        context.EnsureRequiredBindingFailureAudit(descriptor, invocation, SandboxErrorCode.Timeout);
        context.EnsureRequiredBindingFailureAudit(descriptor, invocation, SandboxErrorCode.Timeout);

        Assert.Equal(BindingAuditKinds.SandboxLog, Assert.Single(audit.Events).Kind);
    }

    [Fact]
    public void Custom_sink_fallback_uses_custom_audit_kind_and_remains_idempotent()
    {
        var descriptor = Descriptor() with { AuditKind = BindingAuditKinds.SandboxLog };
        var audit = new ForwardingAuditSink();
        var context = CreateContext(descriptor, audit);
        using var invocation = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint());

        context.EnsureRequiredBindingFailureAudit(descriptor, invocation, SandboxErrorCode.Timeout);
        context.EnsureRequiredBindingFailureAudit(descriptor, invocation, SandboxErrorCode.Timeout);

        Assert.Equal(BindingAuditKinds.SandboxLog, Assert.Single(audit.Events).Kind);
    }

    private static async Task WriteSuccessAfterAsync(
        SandboxContext context,
        Task release,
        string message)
    {
        await release.ConfigureAwait(false);
        WriteAudit(context, success: true, null, message);
    }

    private static SandboxContext CreateContext(BindingDescriptor descriptor, IAuditSink audit)
    {
        var limits = new ResourceLimits(
            MaxFuel: 10_000,
            MaxAllocatedBytes: 1_000_000,
            MaxHostCalls: 10,
            MaxWallTime: TimeSpan.FromSeconds(5));
        var policy = SandboxPolicyBuilder.Create().AllowRuntimeAsync().Build() with
        {
            ResourceLimits = limits
        };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Add(descriptor).Build(),
            audit,
            CancellationToken.None,
            moduleHash: "module-hash",
            policyHash: "policy-hash");
    }

    private static BindingDescriptor Descriptor()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.PureHostFacade,
            static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };

    private static void WriteAudit(
        SandboxContext context,
        bool success,
        SandboxErrorCode? errorCode,
        string message)
    {
        var timestamp = DateTimeOffset.UtcNow;
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            BindingAuditKinds.BindingCall,
            timestamp,
            success,
            BindingId: BindingId,
            Effect: SandboxEffect.Cpu,
            ResourceId: "test:async-audit",
            ErrorCode: errorCode,
            Message: message,
            Fields: context.BindingAuditFields("test", timestamp)));
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ForwardingAuditSink : IAuditSink
    {
        private readonly InMemoryAuditSink _inner = new();

        public IReadOnlyList<SandboxAuditEvent> Events => _inner.Events;
        public long EventsWritten => _inner.EventsWritten;
        public void Write(SandboxAuditEvent auditEvent) => _inner.Write(auditEvent);

        public bool HasBindingAuditSince(
            BindingDescriptor descriptor,
            long checkpoint,
            bool success,
            SandboxErrorCode? expectedErrorCode,
            SandboxRunId runId,
            string moduleHash,
            string policyHash)
            => _inner.HasBindingAuditSince(
                descriptor,
                checkpoint,
                success,
                expectedErrorCode,
                runId,
                moduleHash,
                policyHash);
    }
}
