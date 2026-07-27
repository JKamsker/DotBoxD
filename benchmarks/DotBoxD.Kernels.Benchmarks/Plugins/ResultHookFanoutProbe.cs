namespace DotBoxD.Kernels.Benchmarks.Plugins;

using System.Diagnostics;
using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

internal static class ResultHookFanoutProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;
    private static readonly ProbeEventAdapter Adapter = new();

    public static void Run()
    {
        ValidateGlobalOrdering();

        using var single = Scenario.Create(handlerPipelineCount: 1, includeHandlers: true);
        using var two = Scenario.Create(handlerPipelineCount: 2, includeHandlers: true);
        using var eight = Scenario.Create(handlerPipelineCount: 8, includeHandlers: true);
        using var eightEmpty = Scenario.Create(handlerPipelineCount: 8, includeHandlers: false);

        Warm(single);
        Warm(two);
        Warm(eight);
        Warm(eightEmpty);

        Console.WriteLine("Result-hook fanout probe");
        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine("case                              total ms      ns/op    allocated B      B/op dispatches");
        Write("One pipeline, one handler", Measure(single));
        Write("Two pipelines, one each", Measure(two));
        Write("Eight pipelines, one each", Measure(eight));
        Write("Eight pipelines, empty", Measure(eightEmpty));
    }

    private static void ValidateGlobalOrdering()
    {
        using (var server = PluginServer.Create())
        {
            var first = server.Hooks.On<ProbeEvent>(Adapter);
            var second = server.Hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw));
            AddResult(first, priority: 0, value: 1);
            AddResult(second, priority: 100, value: 2);
            RequireResult(server.Hooks, expected: 2, "cross-pipeline priority");
        }

        using (var server = PluginServer.Create())
        {
            var first = server.Hooks.On<ProbeEvent>(Adapter);
            var second = server.Hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw));
            AddResult(second, priority: 5, value: 2);
            AddResult(first, priority: 5, value: 1);
            RequireResult(server.Hooks, expected: 2, "cross-pipeline install order");
        }

        using (var server = PluginServer.Create())
        {
            var first = server.Hooks.On<ProbeEvent>(Adapter);
            var second = server.Hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw));
            AddPipeline(first, includeHandler: true);
            var beforeRegistration = server.Hooks
                .FireAsync<ProbeEvent, ProbeResult>(new ProbeEvent(1))
                .GetAwaiter()
                .GetResult();
            if (beforeRegistration is not null)
            {
                throw new InvalidOperationException("The pre-registration result-hook dispatch did not abstain.");
            }

            AddResult(second, priority: 10, value: 3);
            RequireResult(server.Hooks, expected: 3, "post-dispatch registration");
        }
    }

    private static void RequireResult(HookRegistry hooks, int expected, string invariant)
    {
        var result = hooks.FireAsync<ProbeEvent, ProbeResult>(new ProbeEvent(1)).GetAwaiter().GetResult();
        if (result is not { Value: var actual } || actual != expected)
        {
            throw new InvalidOperationException(
                $"Result-hook {invariant} invariant failed: expected {expected}, observed {result?.Value}.");
        }
    }

    private static void Warm(Scenario scenario)
    {
        for (var i = 0; i < Warmup; i++)
        {
            scenario.Dispatch();
        }
    }

    private static Measurement Measure(Scenario scenario)
    {
        ForceGc();
        var initialDispatchCount = scenario.DispatchCount;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            scenario.Dispatch();
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var measuredDispatches = scenario.DispatchCount - initialDispatchCount;
        if (measuredDispatches != Iterations)
        {
            throw new InvalidOperationException(
                $"Result-hook dispatch invariant failed: expected {Iterations}, observed {measuredDispatches}.");
        }

        GC.KeepAlive(scenario);
        return new Measurement(elapsed.TotalMilliseconds, allocated, measuredDispatches);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(string name, Measurement measurement) =>
        Console.WriteLine(
            $"{name,-32} {measurement.ElapsedMilliseconds,8:N1} " +
            $"{measurement.ElapsedMilliseconds * 1_000_000 / Iterations,11:N1} " +
            $"{measurement.AllocatedBytes,14:N0} " +
            $"{measurement.AllocatedBytes / (double)Iterations,10:N1} " +
            $"{measurement.DispatchCount,10:N0}");

    private static void AddPipelines(HookRegistry hooks, int count, bool includeHandlers)
    {
        AddPipeline(hooks.On<ProbeEvent>(Adapter), includeHandlers);
        if (count == 1)
        {
            return;
        }

        AddPipeline(hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw)), includeHandlers);
        if (count == 2)
        {
            return;
        }

        if (count != 8)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Expected one, two, or eight pipelines.");
        }

        AddPipeline(hooks.On<ProbeEvent, Context2>(Adapter, static raw => new Context2(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context3>(Adapter, static raw => new Context3(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context4>(Adapter, static raw => new Context4(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context5>(Adapter, static raw => new Context5(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context6>(Adapter, static raw => new Context6(raw)), includeHandlers);
        AddPipeline(hooks.On<ProbeEvent, Context7>(Adapter, static raw => new Context7(raw)), includeHandlers);
    }

    private static void AddPipeline<TContext>(
        HookPipeline<ProbeEvent, TContext> pipeline,
        bool includeHandler)
    {
        if (includeHandler)
        {
            ResultSlot(pipeline).AddDirect(
                priority: 0,
                static (_, _, _) => ValueTask.FromResult<IHookResult?>(null));
        }
    }

    private static void AddResult<TContext>(
        HookPipeline<ProbeEvent, TContext> pipeline,
        int priority,
        int value)
    {
        var result = new ProbeResult(Success: true, Value: value);
        ResultSlot(pipeline).AddDirect(
            priority,
            (_, _, _) => ValueTask.FromResult<IHookResult?>(result));
    }

    private static ResultHookSlot<ProbeEvent, TContext> ResultSlot<TContext>(
        HookPipeline<ProbeEvent, TContext> pipeline) =>
        // Reflection is setup-only and keeps the allocation probe on the real registry path without adding a
        // production benchmark seam. The measured dispatch loop never performs reflective access.
        (ResultHookSlot<ProbeEvent, TContext>)(ResultSlotField<TContext>.Value.GetValue(pipeline) ??
            throw new InvalidOperationException("The result-hook slot was not initialized."));

    private static class ResultSlotField<TContext>
    {
        internal static readonly FieldInfo Value =
            typeof(HookPipeline<ProbeEvent, TContext>).GetField(
                "_resultHooks",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("The result-hook slot field could not be found.");
    }

    private sealed class Scenario : IDisposable
    {
        private readonly PluginServer _server;
        private readonly ProbeEvent _event = new(1);

        private Scenario(PluginServer server)
            => _server = server;

        public long DispatchCount { get; private set; }

        public static Scenario Create(int handlerPipelineCount, bool includeHandlers)
        {
            var server = PluginServer.Create();
            AddPipelines(server.Hooks, handlerPipelineCount, includeHandlers);
            return new Scenario(server);
        }

        public void Dispatch()
        {
            var result = _server.Hooks.FireAsync<ProbeEvent, ProbeResult>(_event).GetAwaiter().GetResult();
            if (result is not null)
            {
                throw new InvalidOperationException("The allocation probe handler unexpectedly produced a result.");
            }

            DispatchCount++;
        }

        public void Dispose() => _server.Dispose();
    }

    private sealed class ProbeEventAdapter : IPluginEventAdapter<ProbeEvent>
    {
        public string EventName => "benchmark.result-hook-fanout";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ProbeEvent e) => [];
    }

    private readonly record struct Measurement(
        double ElapsedMilliseconds,
        long AllocatedBytes,
        long DispatchCount);

    private readonly record struct ProbeEvent(int Value);
    private readonly record struct ProbeResult(bool Success, int Value) : IHookResult;
    private readonly record struct Context1(HookContext Raw);
    private readonly record struct Context2(HookContext Raw);
    private readonly record struct Context3(HookContext Raw);
    private readonly record struct Context4(HookContext Raw);
    private readonly record struct Context5(HookContext Raw);
    private readonly record struct Context6(HookContext Raw);
    private readonly record struct Context7(HookContext Raw);
}
