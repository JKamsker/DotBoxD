using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class BindingAuditPreflightTests
{
    private const string BindingId = "test.audit.preflight";

    public static TheoryData<ExecutionMode> ExecutionModes()
        => new()
        {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(ExecutionModes))]
    public async Task Caller_cancellation_during_preflight_emits_cancelled_audit(ExecutionMode mode)
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = CreateContext(descriptor, audit, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InvokeAsync(mode, context, descriptor));

        var terminal = Assert.Single(audit.Events);
        Assert.False(terminal.Success);
        Assert.Equal(BindingId, terminal.BindingId);
        Assert.Equal(SandboxErrorCode.Cancelled, terminal.ErrorCode);
    }

    [Fact]
    public void Unexpected_compiled_preflight_exception_is_sanitized_and_audited()
    {
        var descriptor = Descriptor();
        var audit = new InMemoryAuditSink();
        var context = CreateContext(descriptor, audit, CancellationToken.None);

        var exception = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.CallBinding(context, BindingId, null!));

        Assert.Equal(SandboxErrorCode.BindingFailure, exception.Error.Code);
        var terminal = Assert.Single(audit.Events);
        Assert.False(terminal.Success);
        Assert.Equal(BindingId, terminal.BindingId);
        Assert.Equal(SandboxErrorCode.BindingFailure, terminal.ErrorCode);
    }

    private static async Task InvokeAsync(
        ExecutionMode mode,
        SandboxContext context,
        BindingDescriptor descriptor)
    {
        if (mode == ExecutionMode.Interpreted)
        {
            _ = await InterpreterBindingCaller.CallAsync(
                context,
                new SandboxExecutionOptions(),
                "module-hash",
                descriptor,
                [],
                "main");
            return;
        }

        _ = CompiledRuntime.CallBinding(context, descriptor.Id, []);
    }

    private static SandboxContext CreateContext(
        BindingDescriptor descriptor,
        InMemoryAuditSink audit,
        CancellationToken cancellationToken)
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
            cancellationToken,
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
            static (_, _, _) => throw new InvalidOperationException("binding must not be invoked"),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
