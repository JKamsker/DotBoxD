using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class BindingAuditCheckpointDispatchTests
{
    private const string BindingId = "test.audit.checkpoint";
    private const string InnerBindingId = "test.audit.checkpoint.inner";
    private const string ModuleHash = "module-hash";
    private const string PolicyHash = "policy-hash";

    public static TheoryData<bool, int> DispatchPaths()
        => new()
        {
            { true, 0 },
            { false, 0 },
            { false, 1 },
            { false, 2 },
            { false, 3 },
        };

    [Theory]
    [MemberData(nameof(DispatchPaths))]
    public async Task No_audit_dispatch_does_not_observe_audit_sink(
        bool interpreted,
        int arity)
    {
        var audit = new ObservedAuditSink();
        var descriptor = Descriptor(arity, AuditLevel.None);
        using var context = CreateContext(descriptor, audit);

        var result = await InvokeAsync(interpreted, arity, context, descriptor);

        Assert.Same(SandboxValue.Unit, result);
        Assert.Equal(0, audit.EventsWrittenReads);
        Assert.Equal(0, audit.HasBindingAuditSinceCalls);
        Assert.Equal(0, audit.WriteCalls);
        Assert.Empty(audit.Events);
    }

    [Theory]
    [MemberData(nameof(DispatchPaths))]
    public async Task Required_audit_dispatch_preserves_exact_checkpoint(
        bool interpreted,
        int arity)
    {
        var audit = new ObservedAuditSink();
        var descriptor = Descriptor(arity, AuditLevel.PerCall);
        using var context = CreateContext(descriptor, audit);
        audit.Seed(CreateSuccessAudit(context, "historical"));
        audit.ResetObservations();

        var result = await InvokeAsync(interpreted, arity, context, descriptor);

        Assert.Same(SandboxValue.Unit, result);
        Assert.Equal(1, audit.EventsWrittenReads);
        Assert.Equal(1, audit.HasBindingAuditSinceCalls);
        Assert.Equal(1, audit.WriteCalls);
        Assert.Equal([1L], audit.ObservedCheckpoints);
        Assert.Collection(
            audit.Events,
            historical => Assert.Equal("historical", historical.Message),
            current => Assert.Equal("current", current.Message));
    }

    [Fact]
    public void Nested_no_audit_dispatch_preserves_outer_audit_scope()
    {
        var audit = new InMemoryAuditSink();
        IAuditSink? observedInnerAudit = null;
        var outerDescriptor = Descriptor(0, AuditLevel.PerCall);
        var innerDescriptor = NestedDescriptor((context, _, _) =>
        {
            observedInnerAudit = context.Audit;
            context.Audit.Write(new SandboxAuditEvent(
                context.RunId,
                "NestedProbe",
                DateTimeOffset.UnixEpoch,
                Success: true,
                BindingId: InnerBindingId));
            return ValueTask.FromResult(SandboxValue.Unit);
        });
        using var context = CreateContext(outerDescriptor, innerDescriptor, audit);
        using var outerInvocation = context.BeginBindingAuditInvocation(
            outerDescriptor,
            context.AuditCheckpoint());
        var outerAudit = context.Audit;

        var result = CompiledRuntime.CallBinding(context, InnerBindingId, []);

        Assert.Same(SandboxValue.Unit, result);
        Assert.Same(outerAudit, observedInnerAudit);
        Assert.Same(outerAudit, context.Audit);
        context.Audit.Write(CreateSuccessAudit(context, "outer success"));
        context.EnsureRequiredBindingSuccessAudit(outerDescriptor, outerInvocation);
        Assert.Collection(
            audit.Events,
            inner => Assert.Equal("NestedProbe", inner.Kind),
            outer => Assert.Equal("outer success", outer.Message));
    }

    private static async Task<SandboxValue> InvokeAsync(
        bool interpreted,
        int arity,
        SandboxContext context,
        BindingDescriptor descriptor)
    {
        var args = Enumerable.Repeat(SandboxValue.Unit, arity).ToArray();
        if (interpreted)
        {
            return await InterpreterBindingCaller.CallAsync(
                context,
                new SandboxExecutionOptions(),
                ModuleHash,
                descriptor,
                args,
                "main");
        }

        return arity switch
        {
            0 => CompiledRuntime.CallBinding(context, BindingId, args),
            1 => CompiledRuntime.CallBinding1(context, BindingId, args[0]),
            2 => CompiledRuntime.CallBinding2(context, BindingId, args[0], args[1]),
            _ => CompiledRuntime.CallBinding3(context, BindingId, args[0], args[1], args[2]),
        };
    }

    private static SandboxContext CreateContext(
        BindingDescriptor descriptor,
        IAuditSink audit)
        => CreateContext(descriptor, secondDescriptor: null, audit);

    private static SandboxContext CreateContext(
        BindingDescriptor descriptor,
        BindingDescriptor? secondDescriptor,
        IAuditSink audit)
    {
        var limits = new ResourceLimits(
            MaxFuel: 1_000_000,
            MaxAllocatedBytes: 1_000_000,
            MaxHostCalls: 100,
            MaxWallTime: TimeSpan.FromSeconds(5));
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        var bindings = new BindingRegistryBuilder().Add(descriptor);
        if (secondDescriptor is not null)
        {
            bindings.Add(secondDescriptor);
        }

        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            bindings.Build(),
            audit,
            CancellationToken.None,
            moduleHash: ModuleHash,
            policyHash: PolicyHash);
    }

    private static BindingDescriptor NestedDescriptor(BindingInvoker invoke)
        => new(
            InnerBindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            invoke,
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));

    private static BindingDescriptor Descriptor(int arity, AuditLevel auditLevel)
    {
        BindingInvoker invoke = auditLevel == AuditLevel.None
            ? static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit)
            : static (context, _, _) =>
            {
                context.Audit.Write(CreateSuccessAudit(context, "current"));
                return ValueTask.FromResult(SandboxValue.Unit);
            };
        return new BindingDescriptor(
            BindingId,
            SemVersion.One,
            Enumerable.Repeat(SandboxType.Unit, arity).ToArray(),
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            auditLevel,
            BindingSafety.PureHostFacade,
            invoke,
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));
    }

    private static SandboxAuditEvent CreateSuccessAudit(
        SandboxContext context,
        string message)
    {
        var timestamp = DateTimeOffset.UnixEpoch;
        return new SandboxAuditEvent(
            context.RunId,
            BindingAuditKinds.BindingCall,
            timestamp,
            Success: true,
            BindingId: BindingId,
            Effect: SandboxEffect.Cpu,
            ResourceId: "test:audit-checkpoint",
            Message: message,
            Fields: context.BindingAuditFields("test", timestamp));
    }

    private sealed class ObservedAuditSink : IAuditSink
    {
        private readonly InMemoryAuditSink _inner = new();

        public int EventsWrittenReads { get; private set; }
        public int HasBindingAuditSinceCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public List<long> ObservedCheckpoints { get; } = [];
        public IReadOnlyList<SandboxAuditEvent> Events => _inner.Events;

        public long EventsWritten
        {
            get
            {
                EventsWrittenReads++;
                return _inner.EventsWritten;
            }
        }

        public void Write(SandboxAuditEvent auditEvent)
        {
            WriteCalls++;
            _inner.Write(auditEvent);
        }

        public bool HasBindingAuditSince(
            BindingDescriptor descriptor,
            long checkpoint,
            bool success,
            SandboxErrorCode? expectedErrorCode,
            SandboxRunId runId,
            string moduleHash,
            string policyHash)
        {
            HasBindingAuditSinceCalls++;
            ObservedCheckpoints.Add(checkpoint);
            return _inner.HasBindingAuditSince(
                descriptor,
                checkpoint,
                success,
                expectedErrorCode,
                runId,
                moduleHash,
                policyHash);
        }

        public void Seed(SandboxAuditEvent auditEvent) => _inner.Write(auditEvent);

        public void ResetObservations()
        {
            EventsWrittenReads = 0;
            HasBindingAuditSinceCalls = 0;
            WriteCalls = 0;
            ObservedCheckpoints.Clear();
        }
    }
}
