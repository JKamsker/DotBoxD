namespace DotBoxD.Kernels.Benchmarks.Plugins;

using System.Diagnostics;
using DotBoxD.Plugins;

internal static class SubscriptionDispatchProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        using var direct = Scenario.DirectPipeline();
        using var single = Scenario.SinglePipeline();
        using var eight = Scenario.EightPipelines();
        using var miss = Scenario.EventMiss();

        _ = Measure(direct, Warmup);
        _ = Measure(single, Warmup);
        _ = Measure(eight, Warmup);
        _ = Measure(miss, Warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Write("Direct empty pipeline control", Measure(direct, Iterations));
        Write("Single pipeline", Measure(single, Iterations));
        Write("Eight event pipelines", Measure(eight, Iterations));
        Write("Event miss", Measure(miss, Iterations));
    }

    private static Measurement Measure(Scenario scenario, int iterations)
    {
        var initialDispatchCount = scenario.DispatchCount;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            scenario.Publish();
        }

        watch.Stop();
        var dispatchCount = scenario.DispatchCount - initialDispatchCount;
        if (dispatchCount != iterations)
        {
            throw new InvalidOperationException(
                $"Subscription dispatch invariant failed: expected {iterations}, observed {dispatchCount}.");
        }

        GC.KeepAlive(scenario);
        return new Measurement(
            watch.Elapsed,
            GC.GetAllocatedBytesForCurrentThread() - before,
            iterations,
            dispatchCount);
    }

    private static void Write(string name, Measurement measurement)
    {
        Console.WriteLine(
            $"{name}: {measurement.Elapsed.TotalMilliseconds:N1} ms, " +
            $"{measurement.Elapsed.TotalNanoseconds / measurement.Iterations:N1} ns/op, " +
            $"{measurement.Allocated:N0} B, " +
            $"{(double)measurement.Allocated / measurement.Iterations:N1} B/op, " +
            $"{measurement.DispatchCount:N0} dispatches");
    }

    private sealed class Scenario : IDisposable
    {
        private readonly PluginServer _server;
        private readonly Action _publish;

        private Scenario(PluginServer server, Action publish)
        {
            _server = server;
            _publish = publish;
        }

        public long DispatchCount { get; private set; }

        public static Scenario DirectPipeline()
        {
            var server = PluginServer.Create();
            var pipeline = server.Subscriptions.On<BenchEvent>();
            return new Scenario(server, () => pipeline.Publish(new BenchEvent(1), CancellationToken.None));
        }

        public static Scenario SinglePipeline()
        {
            var server = PluginServer.Create();
            server.Subscriptions.On<BenchEvent>();
            return new Scenario(server, () => server.Subscriptions.Publish(new BenchEvent(1)));
        }

        public static Scenario EightPipelines()
        {
            var server = PluginServer.Create();
            server.Subscriptions.On<BenchEvent>();
            server.Subscriptions.On<BenchEvent, Context1>(ctx => new Context1(ctx));
            server.Subscriptions.On<BenchEvent, Context2>(ctx => new Context2(ctx));
            server.Subscriptions.On<BenchEvent, Context3>(ctx => new Context3(ctx));
            server.Subscriptions.On<BenchEvent, Context4>(ctx => new Context4(ctx));
            server.Subscriptions.On<BenchEvent, Context5>(ctx => new Context5(ctx));
            server.Subscriptions.On<BenchEvent, Context6>(ctx => new Context6(ctx));
            server.Subscriptions.On<BenchEvent, Context7>(ctx => new Context7(ctx));
            return new Scenario(server, () => server.Subscriptions.Publish(new BenchEvent(1)));
        }

        public static Scenario EventMiss()
        {
            var server = PluginServer.Create();
            server.Subscriptions.On<OtherEvent1>();
            server.Subscriptions.On<OtherEvent2>();
            server.Subscriptions.On<OtherEvent3>();
            server.Subscriptions.On<OtherEvent4>();
            server.Subscriptions.On<OtherEvent5>();
            server.Subscriptions.On<OtherEvent6>();
            server.Subscriptions.On<OtherEvent7>();
            server.Subscriptions.On<OtherEvent8>();
            return new Scenario(server, () => server.Subscriptions.Publish(new BenchEvent(1)));
        }

        public void Publish()
        {
            _publish();
            DispatchCount++;
        }

        public void Dispose() => _server.Dispose();
    }

    private readonly record struct Measurement(
        TimeSpan Elapsed,
        long Allocated,
        int Iterations,
        long DispatchCount);

    private readonly record struct BenchEvent(int Value);

    private readonly record struct OtherEvent1(int Value);

    private readonly record struct OtherEvent2(int Value);

    private readonly record struct OtherEvent3(int Value);

    private readonly record struct OtherEvent4(int Value);

    private readonly record struct OtherEvent5(int Value);

    private readonly record struct OtherEvent6(int Value);

    private readonly record struct OtherEvent7(int Value);

    private readonly record struct OtherEvent8(int Value);

    private readonly record struct Context1(HookContext Raw);

    private readonly record struct Context2(HookContext Raw);

    private readonly record struct Context3(HookContext Raw);

    private readonly record struct Context4(HookContext Raw);

    private readonly record struct Context5(HookContext Raw);

    private readonly record struct Context6(HookContext Raw);

    private readonly record struct Context7(HookContext Raw);
}
