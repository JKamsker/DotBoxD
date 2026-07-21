using System.Buffers;
using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class RpcRequestNameCacheProbe
{
    private const int CacheEntryCount = 128;
    private const int ChurnPayloadCount = 1_024;
    private const int WarmupIterations = 10_000;
    private const int MeasurementIterations = 1_000_000;

    public static void Run()
    {
        var serializer = new MessagePackRpcSerializer();
        var early = CreatePayload("EarlyService", "EarlyMethodX");
        AddToCache(serializer, early);

        const int poisonRequestCount = (CacheEntryCount - 4) / 2;
        for (var i = 0; i < poisonRequestCount; i++)
        {
            AddToCache(serializer, CreatePayload($"PoisonService{i:D3}", $"PoisonMethod{i:D3}"));
        }

        var late = CreatePayload("LateServiceX", "LateMethodXX");
        AddToCache(serializer, late);
        var afterPoison = CreatePayload("MissServiceX", "MissMethodXX");

        Console.WriteLine("RPC request-name cache probe");
        Console.WriteLine(
            $"iterations = {MeasurementIterations:N0}; warmup = {WarmupIterations:N0}; " +
            $"seeded entries = {CacheEntryCount:N0}");
        Write(Measure(serializer, "early warmed names", early));
        Write(Measure(serializer, "late warmed names", late));
        Write(Measure(serializer, "recurring after poison", afterPoison));
        MeasureRegisteredNamesAfterRemoteChurn();
        MeasureUniqueRemoteChurn();
    }

    private static ReadOnlyMemory<byte> CreatePayload(string serviceName, string methodName)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(3);
        writer.Write("MessageId");
        writer.Write(42);
        writer.Write("ServiceName");
        writer.Write(serviceName);
        writer.Write("MethodName");
        writer.Write(methodName);
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }

    private static void AddToCache(
        MessagePackRpcSerializer serializer,
        ReadOnlyMemory<byte> payload) =>
        Observe(serializer.Deserialize<RpcRequest>(payload));

    private static Measurement Measure(
        MessagePackRpcSerializer serializer,
        string name,
        ReadOnlyMemory<byte> payload)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            Observe(serializer.Deserialize<RpcRequest>(payload));
        }

        var anchor = serializer.Deserialize<RpcRequest>(payload);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long checksum = 0;
        var sameReferences = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            var request = serializer.Deserialize<RpcRequest>(payload);
            checksum += Observe(request);
            if (ReferenceEquals(anchor.ServiceName, request.ServiceName) &&
                ReferenceEquals(anchor.MethodName, request.MethodName))
            {
                sameReferences++;
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        ValidateChecksum(name, checksum);

        return new Measurement(name, elapsed.TotalMilliseconds, allocated, sameReferences);
    }

    private static void MeasureRegisteredNamesAfterRemoteChurn()
    {
        var serializer = new MessagePackRpcSerializer();
        var serviceName = new string("GuardService".ToCharArray());
        var methodName = new string("GuardMethodX".ToCharArray());
        var request = new RpcRequest
        {
            MessageId = 42,
            ServiceName = serviceName,
            MethodName = methodName,
        };
        var registrationBuffer = new ArrayBufferWriter<byte>();
        serializer.Serialize(registrationBuffer, request);
        FillWithUniqueRemoteNames(serializer, "F", "G");

        var payload = CreatePayload(serviceName, methodName);
        var decoded = serializer.Deserialize<RpcRequest>(payload);
        var exactReferences = ReferenceEquals(serviceName, decoded.ServiceName) &&
            ReferenceEquals(methodName, decoded.MethodName);
        Write(Measure(serializer, "registered after churn", payload));
        Write(MeasureSerialization(serializer, request));
        Console.WriteLine($"registered exact references: {exactReferences}");
    }

    private static void MeasureUniqueRemoteChurn()
    {
        var serializer = new MessagePackRpcSerializer();
        FillWithUniqueRemoteNames(serializer, "J", "K");
        var payloads = new ReadOnlyMemory<byte>[ChurnPayloadCount];
        for (var i = 0; i < payloads.Length; i++)
        {
            payloads[i] = CreatePayload($"S{i:D11}", $"M{i:D11}");
        }

        for (var i = 0; i < WarmupIterations; i++)
        {
            Observe(serializer.Deserialize<RpcRequest>(payloads[i % payloads.Length]));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            checksum += Observe(serializer.Deserialize<RpcRequest>(payloads[i % payloads.Length]));
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        ValidateChecksum("unique remote churn", checksum);
        Write(new Measurement("unique remote churn", elapsed.TotalMilliseconds, allocated, -1));
    }

    private static Measurement MeasureSerialization(
        MessagePackRpcSerializer serializer,
        RpcRequest request)
    {
        var buffer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < WarmupIterations; i++)
        {
            serializer.Serialize(buffer, request);
            buffer.Clear();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            serializer.Serialize(buffer, request);
            checksum += buffer.WrittenCount;
            buffer.Clear();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (checksum <= 0)
        {
            throw new InvalidOperationException("Registered serialization produced no bytes.");
        }

        return new Measurement("registered serialization", elapsed.TotalMilliseconds, allocated, -1);
    }

    private static void FillWithUniqueRemoteNames(
        MessagePackRpcSerializer serializer,
        string servicePrefix,
        string methodPrefix)
    {
        for (var i = 0; i < ChurnPayloadCount; i++)
        {
            AddToCache(serializer, CreatePayload($"{servicePrefix}{i:D11}", $"{methodPrefix}{i:D11}"));
        }
    }

    private static void ValidateChecksum(string name, long checksum)
    {
        if (checksum != 66L * MeasurementIterations)
        {
            throw new InvalidOperationException($"{name} produced checksum {checksum:N0}.");
        }
    }

    private static int Observe(RpcRequest request) =>
        request.MessageId + request.ServiceName.Length + request.MethodName.Length;

    private static void Write(Measurement measurement)
    {
        var referenceSummary = measurement.SameReferences < 0
            ? "same-ref=n/a"
            : $"same-ref={measurement.SameReferences:N0}";
        Console.WriteLine(
            $"{measurement.Name,-24} {measurement.Milliseconds,9:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op " +
            referenceSummary);
    }

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        int SameReferences)
    {
        public double NanosecondsPerOperation => Milliseconds * 1_000_000 / MeasurementIterations;

        public double BytesPerOperation => AllocatedBytes / (double)MeasurementIterations;
    }
}
