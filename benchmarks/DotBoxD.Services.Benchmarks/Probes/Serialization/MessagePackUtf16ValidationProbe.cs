using System.Buffers;
using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using static DotBoxD.Services.Benchmarks.Probes.MessagePackUtf16ProbeValidators;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class MessagePackUtf16ValidationProbe
{
    private const int WarmupIterations = 20_000;
    private const int MeasurementIterations = 500_000;

    public static void Run()
    {
        var serializer = new MessagePackRpcSerializer();
        const string ascii1 = "a";
        const string ascii4 = "aaaa";
        const string ascii8 = "aaaaaaaa";
        var ascii12 = new string('a', 12);
        var ascii64 = new string('a', 64);
        var ascii1024 = new string('a', 1024);
        var bmp1024 = new string('\u03A9', 1024);
        var astralRepeated = RepeatedAstral(512);
        var astralLate = new string('a', 1022) + "\U0001F680";
        var shortRequest = Request("Calculator", "Add");
        var asciiRequest = Request(new string('s', 128), new string('m', 128));
        var astralRequest = Request(RepeatedAstral(64), RepeatedAstral(64));

        ValidatePayloadSemantics(
            serializer,
            ascii12,
            ascii64,
            ascii1024,
            bmp1024,
            astralRepeated,
            astralLate);

        Console.WriteLine(
            $"iterations = {MeasurementIterations:N0}; warmup = {WarmupIterations:N0}");
        Console.WriteLine("case                                  ms      ns/op    allocated B      B/op checksum");
        WriteValidationSet("empty", string.Empty);
        WriteValidationSet("ASCII 1", ascii1);
        WriteValidationSet("ASCII 4", ascii4);
        WriteValidationSet("ASCII 8", ascii8);
        WriteValidationSet("ASCII 12", ascii12);
        WriteValidationSet("ASCII 1024", ascii1024);
        WriteValidationSet("BMP 1024", bmp1024);
        WriteValidationSet("astral repeated", astralRepeated);
        WriteValidationSet("astral late", astralLate);
        WritePayload(serializer, "payload empty", string.Empty);
        WritePayload(serializer, "payload ASCII 1", ascii1);
        WritePayload(serializer, "payload ASCII 4", ascii4);
        WritePayload(serializer, "payload ASCII 8", ascii8);
        WritePayload(serializer, "payload ASCII 12", ascii12);
        WritePayload(serializer, "payload ASCII 64", ascii64);
        WritePayload(serializer, "payload ASCII 1024", ascii1024);
        WritePayload(serializer, "payload BMP 1024", bmp1024);
        WritePayload(serializer, "payload astral repeated", astralRepeated);
        WritePayload(serializer, "payload astral late", astralLate);
        WriteSerialization("request short names", writer => serializer.Serialize(writer, shortRequest));
        WriteSerialization("request ASCII names", writer => serializer.Serialize(writer, asciiRequest));
        WriteSerialization("request astral names", writer => serializer.Serialize(writer, astralRequest));
        WriteSerialization("direct writer ASCII 12 control", writer => WriteDirect(writer, ascii12));
        Write(MeasureSerialization(
            "direct writer ASCII 1024 control",
            writer => WriteDirect(writer, ascii1024)));
        Write(MeasureSerialization(
            "direct writer astral control",
            writer => WriteDirect(writer, astralRepeated)));
    }

    private static void WriteValidationSet(string name, string value)
    {
        Write(MeasureValidation($"legacy validate {name}", value, LegacyValidate));
        Write(MeasureValidation($"prefilter validate {name}", value, PrefilterValidate));
        Write(MeasureValidation($"strict UTF8 validate {name}", value, StrictValidate));
    }

    private static void WritePayload(MessagePackRpcSerializer serializer, string name, string value)
        => WriteSerialization(name, writer => serializer.Serialize(writer, value));

    private static void WriteSerialization(string name, Action<ArrayBufferWriter<byte>> serialize)
        => Write(MeasureSerialization(name, serialize));

    private static Measurement MeasureValidation(
        string name,
        string value,
        Action<string> validate)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            validate(value);
        }

        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            validate(value);
            checksum += value.Length + 1;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        RequireChecksum(name, checksum, checked((long)(value.Length + 1) * MeasurementIterations));
        GC.KeepAlive(validate);
        return new Measurement(name, elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static Measurement MeasureSerialization(
        string name,
        Action<ArrayBufferWriter<byte>> serialize)
    {
        var writer = new ArrayBufferWriter<byte>(4096);
        serialize(writer);
        var expectedLength = writer.WrittenCount;
        writer.Clear();
        for (var i = 0; i < WarmupIterations; i++)
        {
            serialize(writer);
            writer.Clear();
        }

        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            serialize(writer);
            checksum += writer.WrittenCount;
            writer.Clear();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        RequireChecksum(name, checksum, checked((long)expectedLength * MeasurementIterations));
        GC.KeepAlive(serialize);
        GC.KeepAlive(writer);
        return new Measurement(name, elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static void WriteDirect(ArrayBufferWriter<byte> output, string value)
    {
        var writer = new MessagePackWriter(output);
        writer.Write(value);
        writer.Flush();
    }

    private static void ValidatePayloadSemantics(
        MessagePackRpcSerializer serializer,
        params string[] values)
    {
        foreach (var value in values.Append(string.Empty).Append("\uD7FF\uE000"))
        {
            var validated = Serialize(serializer, value);
            var direct = SerializeDirect(value);
            if (!validated.AsSpan().SequenceEqual(direct) ||
                serializer.Deserialize<string>(validated) != value)
            {
                throw new InvalidOperationException("A valid UTF-16 payload changed wire bytes or failed roundtrip.");
            }
        }

        var malformed = new[]
        {
            new string((char)0xD800, 1),
            new string((char)0xDC00, 1),
            new string((char)0xD800, 1) + "x",
            new string((char)0xDC00, 1) + new string((char)0xD800, 1),
            new string('a', 31) + new string((char)0xD800, 1),
        };
        foreach (var value in malformed)
        {
            var writer = new ArrayBufferWriter<byte>();
            try
            {
                serializer.Serialize(writer, value);
                throw new InvalidOperationException("A malformed UTF-16 payload was accepted.");
            }
            catch (MessagePackSerializationException)
            {
                if (writer.WrittenCount != 0)
                {
                    throw new InvalidOperationException("Malformed UTF-16 wrote bytes before rejection.");
                }
            }
        }
    }

    private static byte[] Serialize(MessagePackRpcSerializer serializer, string value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] SerializeDirect(string value)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteDirect(writer, value);
        return writer.WrittenSpan.ToArray();
    }

    private static RpcRequest Request(string serviceName, string methodName) => new()
    {
        MessageId = 42,
        ServiceName = serviceName,
        MethodName = methodName,
    };

    private static string RepeatedAstral(int pairs)
        => string.Create(pairs * 2, pairs, static (characters, pairCount) =>
        {
            for (var i = 0; i < pairCount; i++)
            {
                characters[i * 2] = '\uD83D';
                characters[(i * 2) + 1] = '\uDE80';
            }
        });

    private static void RequireChecksum(string name, long actual, long expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{name} checksum mismatch: expected {expected:N0}, observed {actual:N0}.");
        }
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-36} {measurement.Milliseconds,8:N1} " +
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
