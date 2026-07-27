using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime.Bindings;

internal static class CompiledBindingTwoArgumentFallbackProbe
{
    private const string BindingId = "probe.regular2";
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        _ = Measure(Warmup, FallbackShape.LegacyTargetTypedCollection);
        _ = Measure(Warmup, FallbackShape.ExplicitArray);
        _ = Measure(Warmup, FallbackShape.ScalarFacade);

        Console.WriteLine("regular fallback controls       iterations      ns/call       B/call    checksum       calls");
        Write("legacy target-typed fallback", Measure(Iterations, FallbackShape.LegacyTargetTypedCollection));
        Write("explicit array fallback", Measure(Iterations, FallbackShape.ExplicitArray));
        Write("regular CallBinding2", Measure(Iterations, FallbackShape.ScalarFacade));
    }

    private static Measurement Measure(int iterations, FallbackShape shape)
    {
        var invoker = new RegularInvoker();
        using var context = CreateContext(invoker);
        var fallback = invoker.Fallback;
        var arg0 = SandboxValue.FromInt32(40);
        var arg1 = SandboxValue.FromInt32(2);
        var checksum = 0L;

        ForceGc();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            CompiledRuntime.ChargeValueArray(context, 2);
            var result = shape switch
            {
                FallbackShape.LegacyTargetTypedCollection =>
                    InvokeFallback(fallback, context, [arg0, arg1]),
                FallbackShape.ExplicitArray =>
                    InvokeFallback(fallback, context, new[] { arg0, arg1 }),
                FallbackShape.ScalarFacade =>
                    CompiledRuntime.CallBinding2(context, BindingId, arg0, arg1),
                _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
            };
            checksum += ((I32Value)result).Value;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        Validate(iterations, checksum, invoker.Calls);
        return new Measurement(
            elapsed.TotalMilliseconds * 1_000_000 / iterations,
            (GC.GetAllocatedBytesForCurrentThread() - before) / (double)iterations,
            checksum,
            invoker.Calls);
    }

    private static SandboxValue InvokeFallback(
        BindingInvoker fallback,
        SandboxContext context,
        IReadOnlyList<SandboxValue> args)
        => fallback(context, args, CancellationToken.None).GetAwaiter().GetResult();

    private static SandboxContext CreateContext(RegularInvoker invoker)
    {
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Add(invoker.Descriptor()).Build(),
            NoopAuditSink.Instance,
            CancellationToken.None);
    }

    private static void Validate(int iterations, long checksum, int calls)
    {
        if (checksum != 40L * iterations || calls != iterations)
        {
            throw new InvalidOperationException(
                $"Fallback invariant changed: checksum={checksum:N0}, calls={calls:N0}.");
        }
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {Iterations,10:N0} {measurement.NanosecondsPerCall,12:N1} " +
            $"{measurement.BytesPerCall,12:N1} {measurement.Checksum,11:N0} " +
            $"{measurement.Calls,11:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class RegularInvoker
    {
        public int Calls { get; private set; }
        public BindingInvoker Fallback => Invoke;

        public BindingDescriptor Descriptor()
            => new(
                BindingId,
                SemVersion.One,
                [SandboxType.I32, SandboxType.I32],
                SandboxType.I32,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                Invoke,
                CompiledBinding.RuntimeStub(
                    typeof(CompiledRuntime).FullName!,
                    nameof(CompiledRuntime.CallBinding)));

        private ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(args[0]);
        }
    }

    private readonly record struct Measurement(
        double NanosecondsPerCall,
        double BytesPerCall,
        long Checksum,
        int Calls);

    private enum FallbackShape
    {
        LegacyTargetTypedCollection,
        ExplicitArray,
        ScalarFacade
    }
}
