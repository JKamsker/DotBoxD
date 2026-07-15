using System.Runtime.CompilerServices;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class KernelRpcClientResponseDecodeProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 100_000;

    public static void Run()
    {
        var scalar = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(42));
        var list = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(
            [KernelRpcValue.Int32(1), KernelRpcValue.Int32(2), KernelRpcValue.Int32(3)]));
        var map = CreateMapPayload(32);
        var nested = CreateRecordListPayload(8);

        Console.WriteLine($"generated client response decodes = {Iterations:N0}");
        Console.WriteLine("case                        median ms    allocated B       B/op   checksum");
        RunCase("scalar I32", scalar, LegacyScalar, DirectScalar, expected: 42);
        RunCase("List<I32>, 3 items", list, LegacyList, DirectList, expected: 9);
        RunCase("Map<String,I32>, 32", map, LegacyMap, DirectMap, expected: 710);
        RunCase("List<Record>, 8 items", nested, LegacyRecords, DirectRecords, expected: 60);
    }

    private static void RunCase(
        string name,
        byte[] payload,
        Func<byte[], int> legacy,
        Func<byte[], int> direct,
        int expected)
    {
        if (legacy(payload) != expected || direct(payload) != expected)
        {
            throw new InvalidOperationException($"{name} response projection changed");
        }

        var pair = KernelRpcClientResponseMeasurement.MeasureAlternating(
            payload,
            legacy,
            direct,
            WarmupIterations,
            Iterations);
        Write(name + " legacy", pair.Legacy);
        Write(name + " direct", pair.Direct);
    }

    private static int LegacyScalar(byte[] payload)
        => KernelRpcBinaryCodec.DecodeValue(payload).Int32Value;

    private static int DirectScalar(byte[] payload)
    {
        Validate(payload);
        var reader = new KernelRpcPayloadReader(payload);
        var result = reader.ReadInt32();
        reader.EnsureConsumed();
        return result;
    }

    private static int LegacyList(byte[] payload) => Checksum(ReadLegacyList(payload));

    private static int DirectList(byte[] payload) => Checksum(ReadDirectList(payload));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<int> ReadLegacyList(byte[] payload)
    {
        var value = KernelRpcBinaryCodec.DecodeValue(payload);
        value.RequireKind(KernelRpcValueKind.List);
        var result = new List<int>(value.ItemCount);
        for (var i = 0; i < value.ItemCount; i++)
        {
            result.Add(value.GetItem(i).Int32Value);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<int> ReadDirectList(byte[] payload)
    {
        Validate(payload);
        var reader = new KernelRpcPayloadReader(payload);
        var count = reader.ReadListHeader();
        var result = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(reader.ReadInt32());
        }

        reader.EnsureConsumed();
        return result;
    }

    private static int LegacyMap(byte[] payload) => Checksum(ReadLegacyMap(payload));

    private static int DirectMap(byte[] payload) => Checksum(ReadDirectMap(payload));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Dictionary<string, int> ReadLegacyMap(byte[] payload)
    {
        var value = KernelRpcBinaryCodec.DecodeValue(payload);
        value.RequireKind(KernelRpcValueKind.Map);
        var result = new Dictionary<string, int>(value.ItemCount / 2);
        for (var i = 0; i < value.ItemCount; i += 2)
        {
            var key = value.GetItem(i).TextValue;
            if (result.ContainsKey(key))
            {
                throw new FormatException("Server extension map payload contains a duplicate key.");
            }

            result.Add(key, value.GetItem(i + 1).Int32Value);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Dictionary<string, int> ReadDirectMap(byte[] payload)
    {
        Validate(payload);
        var reader = new KernelRpcPayloadReader(payload);
        var count = reader.ReadMapHeader();
        var result = new Dictionary<string, int>(count / 2);
        for (var i = 0; i < count; i += 2)
        {
            var key = reader.ReadString();
            if (result.ContainsKey(key))
            {
                throw new FormatException("Server extension map payload contains a duplicate key.");
            }

            result.Add(key, reader.ReadInt32());
        }

        reader.EnsureConsumed();
        return result;
    }

    private static int LegacyRecords(byte[] payload) => Checksum(ReadLegacyRecords(payload));

    private static int DirectRecords(byte[] payload) => Checksum(ReadDirectRecords(payload));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<ClientRecord> ReadLegacyRecords(byte[] payload)
    {
        var value = KernelRpcBinaryCodec.DecodeValue(payload);
        value.RequireKind(KernelRpcValueKind.List);
        var result = new List<ClientRecord>(value.ItemCount);
        for (var i = 0; i < value.ItemCount; i++)
        {
            var item = value.GetItem(i);
            item.RequireKind(KernelRpcValueKind.Record);
            RequireFieldCount(item.ItemCount, 2);
            result.Add(new ClientRecord(item.GetItem(0).Int32Value, item.GetItem(1).TextValue));
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<ClientRecord> ReadDirectRecords(byte[] payload)
    {
        Validate(payload);
        var reader = new KernelRpcPayloadReader(payload);
        var count = reader.ReadListHeader();
        var result = new List<ClientRecord>(count);
        for (var i = 0; i < count; i++)
        {
            RequireFieldCount(reader.ReadRecordHeader(), 2);
            result.Add(new ClientRecord(reader.ReadInt32(), reader.ReadString()));
        }

        reader.EnsureConsumed();
        return result;
    }

    private static void Validate(byte[] payload)
    {
        var reader = new KernelRpcPayloadReader(payload);
        reader.SkipValue();
        reader.EnsureConsumed();
    }

    private static byte[] CreateMapPayload(int count)
    {
        var values = new KernelRpcValue[count * 2];
        for (var i = 0; i < count; i++)
        {
            values[i * 2] = KernelRpcValue.String($"key-{i}");
            values[(i * 2) + 1] = KernelRpcValue.Int32(i);
        }

        return KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Map(values));
    }

    private static byte[] CreateRecordListPayload(int count)
    {
        var values = new KernelRpcValue[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = KernelRpcValue.Record(
                [KernelRpcValue.Int32(i), KernelRpcValue.String("tag")]);
        }

        return KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(values));
    }

    private static int Checksum(List<int> values)
    {
        var checksum = values.Count;
        for (var i = 0; i < values.Count; i++)
        {
            checksum += values[i];
        }

        return checksum;
    }

    private static int Checksum(Dictionary<string, int> values)
    {
        var checksum = values.Count;
        foreach (var pair in values)
        {
            checksum += pair.Key.Length + pair.Value;
        }

        return checksum;
    }

    private static int Checksum(List<ClientRecord> values)
    {
        var checksum = values.Count;
        for (var i = 0; i < values.Count; i++)
        {
            checksum += values[i].Id + values[i].Tag.Length;
        }

        return checksum;
    }

    private static void RequireFieldCount(int actual, int expected)
    {
        if (actual != expected)
        {
            throw new NotSupportedException("Server extension record field count did not match the generated projection shape.");
        }
    }

    private static void Write(
        string name,
        KernelRpcClientResponseMeasurement.Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.Milliseconds,8:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{measurement.BytesPerOperation,10:N1} {measurement.Checksum,10:N0}");

    private readonly record struct ClientRecord(int Id, string Tag);
}
