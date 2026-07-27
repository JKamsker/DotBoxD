using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class RpcPeerFrameDispatchProbe
{
    private const int ConstructionIterations = 200_000;
    private const int DispatchIterations = 5_000_000;
    private const int WarmupIterations = 50_000;

    public static void Run()
    {
        var serializer = new MessagePackRpcSerializer();
        var options = new RpcPeerOptions();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            options,
            streams,
            SendNoopAsync,
            protocolError: static (_, _, _, _) => { },
            dispatchError: static (_, _) => { });
        var outbound = new RpcPeerOutboundInvoker(
            serializer,
            options,
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);

        var construction = MeasureConstruction(inbound, outbound, streams);
        var processor = CreateProcessor(inbound, outbound, streams);
        using var cancel = RpcRawFrame.RentFrame(1, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
        using var response = MessageFramer.FrameMessage(
            serializer,
            2,
            MessageType.Response,
            new RpcResponse { MessageId = 2, IsSuccess = true },
            ReadOnlySpan<byte>.Empty);
        var cancelDispatch = MeasureDispatch(processor, new RpcFrame(cancel), "Cancel frame dispatch");
        var responseDispatch = MeasureDispatch(processor, new RpcFrame(response), "Response frame dispatch");

        Console.WriteLine("RPC peer frame-dispatch probe");
        Console.WriteLine(
            $"construction iterations = {ConstructionIterations:N0}; " +
            $"dispatch iterations = {DispatchIterations:N0}; warmup = {WarmupIterations:N0}");
        Write(construction, ConstructionIterations);
        Write(cancelDispatch, DispatchIterations);
        Write(responseDispatch, DispatchIterations);
    }

    private static Measurement MeasureConstruction(
        RpcPeerInboundDispatcher inbound,
        RpcPeerOutboundInvoker outbound,
        RpcStreamManager streams)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = CreateProcessor(inbound, outbound, streams);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        RpcPeerFrameProcessor? last = null;
        for (var i = 0; i < ConstructionIterations; i++)
        {
            last = CreateProcessor(inbound, outbound, streams);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        GC.KeepAlive(last);
        return new Measurement("Frame processor construction", elapsed, allocated);
    }

    private static Measurement MeasureDispatch(
        RpcPeerFrameProcessor processor,
        RpcFrame frame,
        string name)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            DispatchOnce(processor, frame);
        }

        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var handled = 0;
        for (var i = 0; i < DispatchIterations; i++)
        {
            handled += DispatchOnce(processor, frame);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (handled != DispatchIterations)
        {
            throw new InvalidOperationException(
                $"{name} handled {handled:N0} frames; expected {DispatchIterations:N0}.");
        }

        return new Measurement(name, elapsed, allocated);
    }

    private static int DispatchOnce(RpcPeerFrameProcessor processor, RpcFrame frame)
    {
        var pending = processor.ShouldDisposeAsync(frame, CancellationToken.None);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("The frame handler did not complete synchronously.");
        }

        return pending.Result ? 1 : 0;
    }

    private static RpcPeerFrameProcessor CreateProcessor(
        RpcPeerInboundDispatcher inbound,
        RpcPeerOutboundInvoker outbound,
        RpcStreamManager streams) =>
        new(inbound, outbound, streams, static (_, _, _, _) => { });

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
            $"{measurement.Name,-30} {measurement.Elapsed.TotalMilliseconds,8:N1} ms " +
            $"{measurement.Elapsed.TotalNanoseconds / iterations,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,12:N0} B " +
            $"{measurement.AllocatedBytes / (double)iterations,8:N1} B/op");

    private readonly record struct Measurement(string Name, TimeSpan Elapsed, long AllocatedBytes);
}
