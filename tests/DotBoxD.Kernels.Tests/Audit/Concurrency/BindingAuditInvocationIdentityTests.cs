using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class BindingAuditInvocationIdentityTests
{
    private const string BindingId = "test.async.audit";

    public static TheoryData<SandboxErrorCode> ErrorCodes()
    {
        var codes = new TheoryData<SandboxErrorCode>();
        foreach (var errorCode in Enum.GetValues<SandboxErrorCode>())
        {
            codes.Add(errorCode);
        }

        return codes;
    }

    [Fact]
    public void Overlapping_same_descriptor_invocations_claim_only_their_own_evidence()
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        using var outer = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint());
        var outerAudit = context.Audit;
        using var inner = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint());

        WriteAudit(context, outerAudit, success: true, null, "outer success");
        context.EnsureRequiredBindingSuccessAudit(descriptor, outer);
        var exception = Assert.Throws<SandboxRuntimeException>(
            () => context.EnsureRequiredBindingSuccessAudit(descriptor, inner));
        context.EnsureRequiredBindingFailureAudit(descriptor, inner, exception.Error.Code);

        Assert.Equal(SandboxErrorCode.BindingFailure, exception.Error.Code);
        Assert.Collection(
            audit.Events,
            auditEvent => Assert.Equal("outer success", auditEvent.Message),
            auditEvent => Assert.Equal("binding failed before emitting audit", auditEvent.Message));
    }

    [Fact]
    public void Failed_invocation_does_not_poison_later_same_descriptor_success()
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        IAuditSink firstAudit;

        using (var first = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint()))
        {
            firstAudit = context.Audit;
            var exception = Assert.Throws<SandboxRuntimeException>(
                () => context.EnsureRequiredBindingSuccessAudit(descriptor, first));
            context.EnsureRequiredBindingFailureAudit(descriptor, first, exception.Error.Code);
        }

        using (var second = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint()))
        {
            WriteAudit(context, context.Audit, success: true, null, "second success");
            context.EnsureRequiredBindingSuccessAudit(descriptor, second);
        }

        WriteAudit(context, firstAudit, success: true, null, "late first success");

        Assert.Collection(
            audit.Events,
            auditEvent => Assert.Equal("binding failed before emitting audit", auditEvent.Message),
            auditEvent => Assert.Equal("second success", auditEvent.Message));
    }

    [Fact]
    public void Sealed_success_suppresses_late_terminal_outcomes()
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        using var invocation = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint());
        var invocationAudit = context.Audit;

        WriteAudit(context, invocationAudit, success: true, null, "success");
        context.EnsureRequiredBindingSuccessAudit(descriptor, invocation);
        WriteAudit(context, invocationAudit, success: false, SandboxErrorCode.Timeout, "late failure");
        WriteAudit(context, invocationAudit, success: true, null, "late duplicate success");
        WriteMalformedTerminal(context, invocationAudit);

        var success = Assert.Single(audit.Events);
        Assert.True(success.Success);
        Assert.Equal("success", success.Message);
    }

    [Fact]
    public void Sealed_wrapper_preserves_supplementary_non_binding_audits()
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        using var invocation = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint());
        var invocationAudit = context.Audit;

        WriteAudit(context, invocationAudit, success: true, null, "success");
        context.EnsureRequiredBindingSuccessAudit(descriptor, invocation);
        invocationAudit.Write(new SandboxAuditEvent(
            context.RunId,
            BindingAuditKinds.SandboxLog,
            DateTimeOffset.UtcNow,
            Success: true,
            Message: "late supplementary log"));

        Assert.Equal([BindingAuditKinds.BindingCall, BindingAuditKinds.SandboxLog],
            audit.Events.Select(auditEvent => auditEvent.Kind));
    }

    [Fact]
    public void Sealed_failure_suppresses_late_success_and_different_failure()
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        using var invocation = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint());
        var invocationAudit = context.Audit;

        context.EnsureRequiredBindingFailureAudit(descriptor, invocation, SandboxErrorCode.Timeout);
        context.EnsureRequiredBindingFailureAudit(descriptor, invocation, SandboxErrorCode.Timeout);
        WriteAudit(context, invocationAudit, success: true, null, "late success");
        WriteAudit(
            context,
            invocationAudit,
            success: false,
            SandboxErrorCode.BindingFailure,
            "late different failure");

        var fallback = Assert.Single(audit.Events);
        Assert.False(fallback.Success);
        Assert.Equal(SandboxErrorCode.Timeout, fallback.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(ErrorCodes))]
    public void Each_failure_code_claims_its_matching_evidence(SandboxErrorCode errorCode)
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        using var invocation = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint());

        WriteAudit(context, context.Audit, success: false, errorCode, "detailed failure");
        context.EnsureRequiredBindingFailureAudit(descriptor, invocation, errorCode);

        var detailed = Assert.Single(audit.Events);
        Assert.Equal(errorCode, detailed.ErrorCode);
        Assert.Equal("detailed failure", detailed.Message);
    }

    [Fact]
    public void Raw_destination_evidence_does_not_satisfy_invocation_claim()
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit);
        using var invocation = context.BeginBindingAuditInvocation(descriptor, context.AuditCheckpoint());

        WriteAudit(context, audit, success: true, null, "unowned success");
        var exception = Assert.Throws<SandboxRuntimeException>(
            () => context.EnsureRequiredBindingSuccessAudit(descriptor, invocation));
        context.EnsureRequiredBindingFailureAudit(descriptor, invocation, exception.Error.Code);

        Assert.Collection(
            audit.Events,
            auditEvent => Assert.Equal("unowned success", auditEvent.Message),
            auditEvent => Assert.Equal("binding failed before emitting audit", auditEvent.Message));
    }

    [Fact]
    public void Out_of_order_exit_removes_invocation_from_current_flow()
    {
        var descriptor = Descriptor();
        var outerAuditDestination = new InMemoryAuditSink();
        var outerContext = CreateContext(descriptor, outerAuditDestination);
        var innerAuditDestination = new InMemoryAuditSink();
        var innerContext = CreateContext(descriptor, innerAuditDestination);
        var outer = outerContext.BeginBindingAuditInvocation(descriptor, outerContext.AuditCheckpoint());
        var outerAudit = outerContext.Audit;
        var inner = innerContext.BeginBindingAuditInvocation(descriptor, innerContext.AuditCheckpoint());
        var innerAudit = innerContext.Audit;
        try
        {
            WriteAudit(outerContext, outerAudit, success: true, null, "outer success");
            outerContext.EnsureRequiredBindingSuccessAudit(descriptor, outer);
            outer.Dispose();
            Assert.Same(outerAuditDestination, outerContext.Audit);

            WriteAudit(innerContext, innerAudit, success: true, null, "inner success");
            innerContext.EnsureRequiredBindingSuccessAudit(descriptor, inner);
            inner.Dispose();

            Assert.Same(innerAuditDestination, innerContext.Audit);
            Assert.Single(outerAuditDestination.Events);
            Assert.Single(innerAuditDestination.Events);
        }
        finally
        {
            inner.Dispose();
            outer.Dispose();
        }
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
        IAuditSink audit,
        bool success,
        SandboxErrorCode? errorCode,
        string message)
    {
        var timestamp = DateTimeOffset.UtcNow;
        audit.Write(new SandboxAuditEvent(
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

    private static void WriteMalformedTerminal(SandboxContext context, IAuditSink audit)
        => audit.Write(new SandboxAuditEvent(
            context.RunId,
            BindingAuditKinds.BindingCall,
            DateTimeOffset.UtcNow,
            Success: false,
            BindingId: BindingId,
            ErrorCode: SandboxErrorCode.Timeout,
            Message: "late malformed terminal"));
}
