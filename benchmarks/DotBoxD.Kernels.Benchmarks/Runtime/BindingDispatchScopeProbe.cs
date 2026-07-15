using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class BindingDispatchScopeProbe
{
    private const string BindingId = "probe.unit";
    private const int Warmup = 20_000;
    private const int Iterations = 500_000;

    public static void Run()
    {
        _ = Measure(Warmup);
        _ = MeasureAudited(Warmup, isAsync: false);
        _ = MeasureAudited(Warmup, isAsync: true);

        var measurement = Measure(Iterations);
        var auditedSync = MeasureAudited(Iterations, isAsync: false);
        var auditedAsyncCompleted = MeasureAudited(Iterations, isAsync: true);
        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine(
            $"CompiledRuntime.CallBinding no-op {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.HostCalls,12:N0} calls");
        WriteMeasurement("audited sync", auditedSync);
        WriteMeasurement("audited async-completed", auditedAsyncCompleted);
    }

    private static void WriteMeasurement(string label, Measurement measurement)
        => Console.WriteLine(
            $"CompiledRuntime.CallBinding {label,-23} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.HostCalls,12:N0} calls");

    private static Measurement Measure(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var context = CreateContext();
        var args = Array.Empty<SandboxValue>();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            _ = CompiledRuntime.CallBinding(context, BindingId, args);
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            context.Budget.HostCalls);
    }

    private static Measurement MeasureAudited(int iterations, bool isAsync)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var (context, invoker) = CreateAuditedContext(isAsync);
        var args = Array.Empty<SandboxValue>();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            _ = CompiledRuntime.CallBinding(context, BindingId, args);
        }

        sw.Stop();
        GC.KeepAlive(invoker);
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            context.Budget.HostCalls);
    }

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxHostCalls: int.MaxValue,
            MaxWallTime: TimeSpan.FromMinutes(5));
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Add(Descriptor()).Build(),
            NoopAuditSink.Instance,
            CancellationToken.None);
    }

    private static (SandboxContext Context, AuditInvoker Invoker) CreateAuditedContext(bool isAsync)
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxHostCalls: int.MaxValue,
            MaxWallTime: TimeSpan.FromMinutes(5));
        var policy = SandboxPolicyBuilder.Create().AllowRuntimeAsync().Build() with { ResourceLimits = limits };
        var invoker = new AuditInvoker();
        var descriptor = Descriptor(invoker.Invoke, AuditLevel.PerCall) with { IsAsync = isAsync };
        var context = new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Add(descriptor).Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
        invoker.Initialize(context);
        return (context, invoker);
    }

    private static BindingDescriptor Descriptor()
        => Descriptor(static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit), AuditLevel.None);

    private static BindingDescriptor Descriptor(BindingInvoker invoke, AuditLevel auditLevel)
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            auditLevel,
            BindingSafety.PureHostFacade,
            invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private sealed class AuditInvoker
    {
        private SandboxAuditEvent? _auditEvent;

        public void Initialize(SandboxContext context)
        {
            var timestamp = DateTimeOffset.UnixEpoch;
            _auditEvent = new SandboxAuditEvent(
                context.RunId,
                BindingAuditKinds.BindingCall,
                timestamp,
                Success: true,
                BindingId: BindingId,
                Effect: SandboxEffect.Cpu,
                ResourceId: "probe:unit",
                Fields: context.BindingAuditFields("probe", timestamp));
        }

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> _,
            CancellationToken __)
        {
            context.Audit.Write(_auditEvent!);
            return ValueTask.FromResult(SandboxValue.Unit);
        }
    }

    private sealed class NoopAuditSink : IAuditSink
    {
        public static NoopAuditSink Instance { get; } = new();
        public long EventsWritten => 0;
        public void Write(SandboxAuditEvent auditEvent) { }
        public bool HasBindingAuditSince(
            BindingDescriptor descriptor,
            long checkpoint,
            bool success,
            SandboxErrorCode? expectedErrorCode,
            SandboxRunId runId,
            string moduleHash,
            string policyHash)
            => false;
    }

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int HostCalls);
}
