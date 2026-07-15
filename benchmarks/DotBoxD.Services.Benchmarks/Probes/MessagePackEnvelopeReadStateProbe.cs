using System.Buffers;
using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class MessagePackEnvelopeReadStateProbe
{
    private const int WarmupIterations = 100_000;
    private const int MeasurementIterations = 1_000_000;

    public static void Run()
    {
        var serializer = new MessagePackRpcSerializer();
        var requestPayload = Serialize(
            serializer,
            new RpcRequest
            {
                MessageId = 42,
                ServiceName = "GameService",
                MethodName = "MovePlayerAsync",
            });
        var responsePayload = Serialize(
            serializer,
            new RpcResponse { MessageId = 42, IsSuccess = true });

        Warmup(serializer, requestPayload, responsePayload);
        Console.WriteLine(
            $"iterations = {MeasurementIterations:N0}; warmup = {WarmupIterations:N0}");
        MeasureRequests(serializer, requestPayload);
        MeasureResponses(serializer, responsePayload);
    }

    private static ReadOnlyMemory<byte> Serialize<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        return writer.WrittenMemory.ToArray();
    }

    private static void Warmup(
        MessagePackRpcSerializer serializer,
        ReadOnlyMemory<byte> requestPayload,
        ReadOnlyMemory<byte> responsePayload)
    {
        long checksum = 0;
        for (var i = 0; i < WarmupIterations; i++)
        {
            checksum += Observe(serializer.Deserialize<RpcRequest>(requestPayload));
            checksum += Observe(serializer.Deserialize<RpcResponse>(responsePayload));
        }

        GC.KeepAlive(checksum);
    }

    private static void MeasureRequests(
        MessagePackRpcSerializer serializer,
        ReadOnlyMemory<byte> payload)
    {
        long checksum = 0;
        var stopwatch = Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            checksum += Observe(serializer.Deserialize<RpcRequest>(payload));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        stopwatch.Stop();
        Print("request envelope", stopwatch.Elapsed, allocated, checksum);
    }

    private static void MeasureResponses(
        MessagePackRpcSerializer serializer,
        ReadOnlyMemory<byte> payload)
    {
        long checksum = 0;
        var stopwatch = Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            checksum += Observe(serializer.Deserialize<RpcResponse>(payload));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        stopwatch.Stop();
        Print("response envelope", stopwatch.Elapsed, allocated, checksum);
    }

    private static int Observe(RpcRequest request) =>
        request.MessageId +
        request.ServiceName.Length +
        request.MethodName.Length +
        (request.InstanceId?.Length ?? 0) +
        (request.Streams?.Length ?? 0);

    private static int Observe(RpcResponse response) =>
        response.MessageId +
        (response.IsSuccess ? 1 : 0) +
        (response.ErrorMessage?.Length ?? 0) +
        (response.ErrorType?.Length ?? 0) +
        (response.Stream?.StreamId ?? 0);

    private static void Print(string name, TimeSpan elapsed, long allocated, long checksum) =>
        Console.WriteLine(
            $"{name,-18} {elapsed.TotalMilliseconds,9:N1} ms " +
            $"{allocated,14:N0} B {allocated / (double)MeasurementIterations,6:N1} B/decode " +
            $"checksum={checksum:N0}");
}
