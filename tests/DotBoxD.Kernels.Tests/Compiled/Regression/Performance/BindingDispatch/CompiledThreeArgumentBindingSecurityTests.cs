using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.BindingDispatch;

public sealed class CompiledThreeArgumentBindingSecurityTests
{
    [Fact]
    public void Async_declared_binding_is_denied_before_fast_invocation_without_grant()
    {
        var invoker = new ControlledBinding(throws: false);
        var descriptor = invoker.Descriptor(AuditLevel.None) with { IsAsync = true };
        using var context = Context(descriptor, new InMemoryAuditSink());

        var error = Assert.Throws<SandboxRuntimeException>(
            () => Invoke(context, descriptor.Id));

        Assert.Equal(SandboxErrorCode.PermissionDenied, error.Error.Code);
        Assert.Equal(0, invoker.FastCalls);
        Assert.Equal(0, invoker.ListCalls);
    }

    [Fact]
    public void Fast_invoker_failure_is_sanitized_and_writes_required_audit()
    {
        var audit = new InMemoryAuditSink();
        var invoker = new ControlledBinding(throws: true);
        var descriptor = invoker.Descriptor(AuditLevel.PerCall);
        using var context = Context(descriptor, audit);

        var error = Assert.Throws<SandboxRuntimeException>(
            () => Invoke(context, descriptor.Id));

        Assert.Equal(SandboxErrorCode.BindingFailure, error.Error.Code);
        Assert.DoesNotContain("secret", error.Error.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(1, invoker.FastCalls);
        Assert.Equal(0, invoker.ListCalls);
        var auditEvent = Assert.Single(audit.Events);
        Assert.False(auditEvent.Success);
        Assert.Equal(descriptor.Id, auditEvent.BindingId);
        Assert.Equal(SandboxErrorCode.BindingFailure, auditEvent.ErrorCode);
    }

    private static SandboxValue Invoke(SandboxContext context, string id)
        => CompiledRuntime.CallBinding3(
            context,
            id,
            SandboxValue.FromInt32(1),
            SandboxValue.FromInt32(2),
            SandboxValue.FromInt32(3));

    private static SandboxContext Context(BindingDescriptor descriptor, IAuditSink audit)
    {
        var limits = new ResourceLimits(MaxFuel: 1_000_000, MaxAllocatedBytes: 1_000_000);
        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Add(descriptor).Build(),
            audit,
            CancellationToken.None);
    }

    private sealed class ControlledBinding(bool throws) : IThreeArgumentBindingInvoker
    {
        public int FastCalls { get; private set; }
        public int ListCalls { get; private set; }

        public BindingDescriptor Descriptor(AuditLevel auditLevel)
            => new(
                "test.secure3",
                SemVersion.One,
                [SandboxType.I32, SandboxType.I32, SandboxType.I32],
                SandboxType.I32,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                auditLevel,
                BindingSafety.PureHostFacade,
                Invoke,
                CompiledBinding.RuntimeStub(
                    typeof(CompiledRuntime).FullName!,
                    nameof(CompiledRuntime.CallBinding)));

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Complete();
        }

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            SandboxValue arg1,
            SandboxValue arg2,
            CancellationToken cancellationToken)
        {
            FastCalls++;
            return Complete();
        }

        private ValueTask<SandboxValue> Complete()
            => throws
                ? throw new InvalidOperationException("secret host failure")
                : ValueTask.FromResult(SandboxValue.FromInt32(6));
    }
}
