using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class DisabledStreamingContextProbe
{
    private const int SerialIterations = 20_000_000;
    private const int ParallelIterationsPerWorker = 2_000_000;
    private const int ParallelWorkers = 4;
    private const int WarmupIterations = 100_000;

    public static void Run()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var enabled = new RpcStreamingContext(streams, serializer, CancellationToken.None);
        for (var i = 0; i < WarmupIterations; i++)
        {
            CompleteContext(RpcStreamingContext.Disabled);
            CompleteContext(enabled);
        }

        var serial = MeasureSerial(RpcStreamingContext.Disabled, "Serial disabled completion");
        var parallel = MeasureParallel(RpcStreamingContext.Disabled, "Parallel disabled completion");
        var enabledSerial = MeasureSerial(enabled, "Serial enabled control");
        var enabledParallel = MeasureParallel(enabled, "Parallel enabled control");

        Console.WriteLine("Disabled streaming-context probe");
        Console.WriteLine(
            $"serial iterations = {SerialIterations:N0}; parallel workers = {ParallelWorkers}; " +
            $"parallel iterations/worker = {ParallelIterationsPerWorker:N0}");
        Write(serial, SerialIterations);
        Write(parallel, ParallelWorkers * ParallelIterationsPerWorker);
        Write(enabledSerial, SerialIterations);
        Write(enabledParallel, ParallelWorkers * ParallelIterationsPerWorker);
    }

    private static Measurement MeasureSerial(RpcStreamingContext context, string name)
    {
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < SerialIterations; i++)
        {
            CompleteContext(context);
        }

        return new Measurement(
            name,
            Stopwatch.GetElapsedTime(started),
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static Measurement MeasureParallel(RpcStreamingContext context, string name)
    {
        using var ready = new CountdownEvent(ParallelWorkers);
        using var start = new ManualResetEventSlim();
        var workers = new Thread[ParallelWorkers];
        for (var i = 0; i < workers.Length; i++)
        {
            workers[i] = new Thread(() => RunWorker(context, ready, start))
            {
                IsBackground = true,
                Name = $"disabled-streaming-context-{i}",
            };
            workers[i].Start();
        }

        ready.Wait();
        ForceGc();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        start.Set();
        foreach (var worker in workers)
        {
            worker.Join();
        }

        return new Measurement(
            name,
            Stopwatch.GetElapsedTime(started),
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
    }

    private static void RunWorker(
        RpcStreamingContext context,
        CountdownEvent ready,
        ManualResetEventSlim start)
    {
        ready.Signal();
        start.Wait();
        for (var i = 0; i < ParallelIterationsPerWorker; i++)
        {
            CompleteContext(context);
        }
    }

    private static void CompleteContext(RpcStreamingContext context)
    {
        if (context.CompleteDispatch() is not null)
        {
            throw new InvalidOperationException("The context returned an unexpected response stream.");
        }
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(Measurement measurement, int iterations) =>
        Console.WriteLine(
            $"{measurement.Name,-31} {measurement.Elapsed.TotalMilliseconds,9:N1} ms " +
            $"{measurement.Elapsed.TotalNanoseconds / iterations,8:N2} ns/op " +
            $"{measurement.AllocatedBytes,12:N0} B " +
            $"{measurement.AllocatedBytes / (double)iterations,8:N4} B/op");

    private readonly record struct Measurement(string Name, TimeSpan Elapsed, long AllocatedBytes);
}
