using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterBindingArityProbe
{
    private const int WarmupIterations = 10_000;
    private const int Iterations = 100_000;
    private static readonly SandboxExecutionOptions Options = new()
    {
        Mode = ExecutionMode.Interpreted,
        RunId = SandboxRunId.New(),
        SuppressSuccessfulRunSummaryAudit = true
    };

    public static async Task RunAsync()
    {
        var regularOne = await MeasureAsync("regular one argument", arity: 1, useFastTarget: false);
        var fastOne = await MeasureAsync("fast-capable one argument", arity: 1, useFastTarget: true);
        var regularTwo = await MeasureAsync("regular two arguments", arity: 2, useFastTarget: false);
        var fastTwo = await MeasureAsync("fast-capable two arguments", arity: 2, useFastTarget: true);

        AssertSameResources(regularOne, fastOne);
        AssertSameResources(regularTwo, fastTwo);

        Console.WriteLine("path                          ns/op       B/op   list calls   fast calls");
        Write(regularOne);
        Write(fastOne);
        Write(regularTwo);
        Write(fastTwo);
    }

    private static async Task<Measurement> MeasureAsync(string name, int arity, bool useFastTarget)
    {
        var invoker = useFastTarget ? new FastBinding() : null;
        var regular = useFastTarget ? null : new RegularBinding();
        var descriptor = useFastTarget
            ? invoker!.Descriptor(arity)
            : regular!.Descriptor(arity);
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(descriptor);
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(ModuleJson(arity));
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = Execute(host, plan, arity);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        long checksum = 0;
        SandboxResourceUsage? resourceUsage = null;
        for (var i = 0; i < Iterations; i++)
        {
            var result = Execute(host, plan, arity);
            checksum += ((I32Value)result.Value!).Value;
            resourceUsage = result.ResourceUsage;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var expectedCalls = WarmupIterations + Iterations;
        var listCalls = invoker?.ListCalls ?? regular!.ListCalls;
        var fastCalls = invoker?.FastCalls ?? 0;
        if (listCalls + fastCalls != expectedCalls ||
            checksum != (long)(arity == 1 ? 41 : 42) * Iterations)
        {
            throw new InvalidOperationException("Interpreter binding probe invariants changed.");
        }

        return new Measurement(
            name,
            elapsed,
            allocated,
            listCalls,
            fastCalls,
            resourceUsage!);
    }

    private static SandboxExecutionResult Execute(SandboxHost host, ExecutionPlan plan, int arity)
    {
        var pending = host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Interpreter binding probe did not complete synchronously.");
        }

        var result = pending.Result;
        if (!result.Succeeded ||
            result.ActualMode != ExecutionMode.Interpreted ||
            ((I32Value)result.Value!).Value != (arity == 1 ? 41 : 42))
        {
            throw new InvalidOperationException(result.Error?.SafeMessage ?? "Interpreter binding result changed.");
        }

        return result;
    }

    private static void AssertSameResources(Measurement regular, Measurement fast)
    {
        if (regular.ResourceUsage != fast.ResourceUsage)
        {
            throw new InvalidOperationException(
                $"Resource usage differs between {regular.Name} and {fast.Name}.");
        }
    }

    private static string ModuleJson(int arity)
    {
        var call = arity == 1
            ? "{ \"call\": \"probe.binding\", \"args\": [{ \"i32\": 41 }] }"
            : "{ \"call\": \"probe.binding\", \"args\": [{ \"i32\": 20 }, { \"i32\": 22 }] }";
        return $$"""
        {
          "id": "interpreter-binding-arity-{{arity}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "I32",
            "body": [{ "op": "return", "value": {{call}} }]
          }]
        }
        """;
    }

    private static BindingDescriptor Descriptor(int arity, BindingInvoker invoke)
        => new(
            "probe.binding",
            SemVersion.One,
            arity == 1 ? [SandboxType.I32] : [SandboxType.I32, SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private sealed class FastBinding : IOneArgumentBindingInvoker, ITwoArgumentBindingInvoker
    {
        public int ListCalls { get; private set; }
        public int FastCalls { get; private set; }

        public BindingDescriptor Descriptor(int arity)
            => InterpreterBindingArityProbe.Descriptor(arity, InvokeList);

        private ValueTask<SandboxValue> InvokeList(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return args.Count == 1
                ? ValueTask.FromResult(args[0])
                : Add(args[0], args[1]);
        }

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            CancellationToken cancellationToken)
        {
            FastCalls++;
            return ValueTask.FromResult(arg0);
        }

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            SandboxValue arg1,
            CancellationToken cancellationToken)
        {
            FastCalls++;
            return Add(arg0, arg1);
        }
    }

    private sealed class RegularBinding
    {
        public int ListCalls { get; private set; }

        public BindingDescriptor Descriptor(int arity)
            => InterpreterBindingArityProbe.Descriptor(arity, Invoke);

        private ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return args.Count == 1
                ? ValueTask.FromResult(args[0])
                : Add(args[0], args[1]);
        }
    }

    private static ValueTask<SandboxValue> Add(SandboxValue left, SandboxValue right)
        => ValueTask.FromResult(SandboxValue.FromInt32(
            ((I32Value)left).Value + ((I32Value)right).Value));

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-27} {measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.BytesPerOperation,10:N1} {measurement.ListCalls,12:N0} " +
            $"{measurement.FastCalls,12:N0}");

    private readonly record struct Measurement(
        string Name,
        TimeSpan Elapsed,
        long AllocatedBytes,
        int ListCalls,
        int FastCalls,
        SandboxResourceUsage ResourceUsage)
    {
        public double NanosecondsPerOperation => Elapsed.TotalNanoseconds / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}
