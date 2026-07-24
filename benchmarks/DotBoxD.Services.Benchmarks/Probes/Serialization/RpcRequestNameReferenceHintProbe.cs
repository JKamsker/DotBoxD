using System.Buffers;
using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class RpcRequestNameReferenceHintProbe
{
    private const int MeasurementIterations = 500_000;
    private const int WarmupIterations = 20_000;

    public static void Run()
    {
        var shortRequest = Request("Calculator", "Add");
        var equalShort = Request(Distinct(shortRequest.ServiceName), Distinct(shortRequest.MethodName));
        var alternating = new[]
        {
            Request(new string('s', 128), new string('m', 128)),
            Request(new string('t', 128), new string('n', 128)),
        };

        Console.WriteLine($"iterations = {MeasurementIterations:N0}; warmup = {WarmupIterations:N0}");
        Console.WriteLine("case                              ms      ns/op    allocated B      B/op checksum");
        Write(Measure("stable short", [shortRequest]));
        Write(Measure("stable ASCII x128", [Request(new string('s', 128), new string('m', 128))]));
        Write(Measure("stable two-byte x128", [Request(new string('\u00e9', 128), new string('\u00f8', 128))]));
        Write(Measure("stable astral x64", [Request(Repeat("\U0001f600", 64), Repeat("\U0001f680", 64))]));
        Write(Measure("alternating registered x2", alternating));
        Write(Measure("cycling registered x128", CyclingRequests()));
        Write(Measure("equal distinct references", [equalShort], registrations: [shortRequest]));
        Write(Measure("uncached ASCII 257", [Request(new string('s', 257), new string('m', 257))]));
        Write(Measure("uncached two-byte 129", [Request(new string('\u00e9', 129), new string('\u00f8', 129))]));
        Write(Measure("OldSpec stable ASCII x128", [Request(new string('s', 128), new string('m', 128))], oldSpec: true));
        Write(Measure("stable after remote churn", [shortRequest], remoteChurn: true));
    }

    private static Measurement Measure(
        string name,
        RpcRequest[] requests,
        RpcRequest[]? registrations = null,
        bool oldSpec = false,
        bool remoteChurn = false)
    {
        var options = MessagePackRpcSerializer.CreateOptions().WithOldSpec(oldSpec);
        var serializer = new MessagePackRpcSerializer(options);
        var output = new ArrayBufferWriter<byte>(4096);
        foreach (var request in registrations ?? requests)
        {
            SerializeAndValidate(serializer, output, request, oldSpec);
        }

        if (remoteChurn)
        {
            FillRemoteCache(serializer);
        }

        var lengths = requests.Select(request => Canonical(request, oldSpec).Length).ToArray();
        for (var i = 0; i < WarmupIterations; i++)
        {
            serializer.Serialize(output, requests[i % requests.Length]);
            output.Clear();
        }

        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            var index = i % requests.Length;
            serializer.Serialize(output, requests[index]);
            checksum += output.WrittenCount;
            output.Clear();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var expectedChecksum = ExpectedChecksum(lengths);
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} checksum mismatch: expected {expectedChecksum:N0}, observed {checksum:N0}.");
        }

        return new Measurement(name, elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static RpcRequest[] CyclingRequests()
    {
        const string service = "SharedCyclingService";
        var requests = new RpcRequest[128];
        requests[0] = Request(service, service);
        for (var i = 1; i < requests.Length; i++)
        {
            requests[i] = Request(service, $"CyclingMethod{i:D3}");
        }

        return requests;
    }

    private static void FillRemoteCache(MessagePackRpcSerializer serializer)
    {
        for (var i = 0; i < 1_024; i++)
        {
            var payload = RawRequest($"RemoteService{i:D4}", $"RemoteMethod{i:D4}");
            _ = serializer.Deserialize<RpcRequest>(payload);
        }
    }

    private static byte[] RawRequest(string serviceName, string methodName)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(output);
        writer.WriteMapHeader(3);
        writer.Write("MessageId");
        writer.Write(42);
        writer.Write("ServiceName");
        writer.Write(serviceName);
        writer.Write("MethodName");
        writer.Write(methodName);
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    private static void SerializeAndValidate(
        MessagePackRpcSerializer serializer,
        ArrayBufferWriter<byte> output,
        RpcRequest request,
        bool oldSpec)
    {
        serializer.Serialize(output, request);
        if (!output.WrittenSpan.SequenceEqual(Canonical(request, oldSpec)))
        {
            throw new InvalidOperationException("Registered request names changed canonical wire bytes.");
        }

        output.Clear();
    }

    private static byte[] Canonical(RpcRequest request, bool oldSpec)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(output) { OldSpec = oldSpec };
        writer.WriteMapHeader(5);
        writer.Write("MessageId");
        writer.Write(request.MessageId);
        writer.Write("ServiceName");
        writer.Write(request.ServiceName);
        writer.Write("MethodName");
        writer.Write(request.MethodName);
        writer.Write("InstanceId");
        writer.Write(request.InstanceId);
        writer.Write("Streams");
        writer.WriteNil();
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    private static long ExpectedChecksum(int[] lengths)
    {
        var fullCycles = MeasurementIterations / lengths.Length;
        var remainder = MeasurementIterations % lengths.Length;
        long checksum = (long)fullCycles * lengths.Sum();
        for (var i = 0; i < remainder; i++)
        {
            checksum += lengths[i];
        }

        return checksum;
    }

    private static RpcRequest Request(string serviceName, string methodName) => new()
    {
        MessageId = 42,
        ServiceName = serviceName,
        MethodName = methodName,
    };

    private static string Distinct(string value) => new(value.ToCharArray());

    private static string Repeat(string value, int count) => string.Concat(Enumerable.Repeat(value, count));

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-32} {measurement.Milliseconds,8:N1} " +
            $"{measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.AllocatedBytes,14:N0} " +
            $"{measurement.BytesPerOperation,9:N1} {measurement.Checksum,12:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        long Checksum)
    {
        public double NanosecondsPerOperation =>
            Milliseconds * 1_000_000 / MeasurementIterations;

        public double BytesPerOperation =>
            AllocatedBytes / (double)MeasurementIterations;
    }
}
