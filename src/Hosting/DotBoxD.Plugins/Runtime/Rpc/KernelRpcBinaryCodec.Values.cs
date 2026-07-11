namespace DotBoxD.Plugins;

using System.Buffers;

public static partial class KernelRpcBinaryCodec
{
    private static void WriteValue(IBufferWriter<byte> writer, KernelRpcValue value, int depth, ref int itemCount)
    {
        WriteByte(writer, (byte)value.Kind);
        if (TryWriteScalarValue(writer, value))
        {
            return;
        }

        if (TryWriteItemValue(writer, value, depth, ref itemCount))
        {
            return;
        }

        throw new NotSupportedException($"Server extension value kind '{value.Kind}' is not supported.");
    }

    private static bool TryWriteScalarValue(IBufferWriter<byte> writer, KernelRpcValue value)
    {
        switch (value.Kind)
        {
            case KernelRpcValueKind.Unit:
                return true;
            case KernelRpcValueKind.Bool:
                WriteByte(writer, value.BoolValue ? (byte)1 : (byte)0);
                return true;
            case KernelRpcValueKind.I32:
                WriteInt32(writer, value.Int32Value);
                return true;
            case KernelRpcValueKind.I64:
                WriteInt64(writer, value.Int64Value);
                return true;
            case KernelRpcValueKind.F64:
                WriteInt64(writer, BitConverter.DoubleToInt64Bits(value.DoubleValue));
                return true;
            case KernelRpcValueKind.String:
                WriteString(writer, value.TextValue);
                return true;
            case KernelRpcValueKind.Guid:
                WriteGuid(writer, value.GuidValue);
                return true;
            default:
                return false;
        }
    }

    private static bool TryWriteItemValue(
        IBufferWriter<byte> writer,
        KernelRpcValue value,
        int depth,
        ref int itemCount)
    {
        switch (value.Kind)
        {
            case KernelRpcValueKind.List:
            case KernelRpcValueKind.Record:
            case KernelRpcValueKind.Map:
                WriteItems(writer, value.ItemSpan, depth, ref itemCount);
                return true;
            default:
                return false;
        }
    }

    private static KernelRpcValue ReadValue(ref Reader reader, int depth)
    {
        var kind = (KernelRpcValueKind)reader.ReadByte();
        if (TryReadScalarValue(ref reader, kind, out var scalar))
        {
            return scalar;
        }

        if (TryReadItemValue(ref reader, kind, depth, out var item))
        {
            return item;
        }

        throw new FormatException($"Server extension payload contains unknown value kind '{kind}'.");
    }

    private static bool TryReadScalarValue(
        ref Reader reader,
        KernelRpcValueKind kind,
        out KernelRpcValue value)
    {
        value = kind switch
        {
            KernelRpcValueKind.Unit => KernelRpcValue.Unit(),
            KernelRpcValueKind.Bool => ReadBool(ref reader),
            KernelRpcValueKind.I32 => KernelRpcValue.Int32(reader.ReadInt32()),
            KernelRpcValueKind.I64 => KernelRpcValue.Int64(reader.ReadInt64()),
            KernelRpcValueKind.F64 => ReadDouble(ref reader),
            KernelRpcValueKind.String => KernelRpcValue.String(reader.ReadString()),
            KernelRpcValueKind.Guid => KernelRpcValue.Guid(reader.ReadGuid()),
            _ => default
        };
        return value.Kind != default || kind == KernelRpcValueKind.Unit;
    }

    private static bool TryReadItemValue(
        ref Reader reader,
        KernelRpcValueKind kind,
        int depth,
        out KernelRpcValue value)
    {
        value = kind switch
        {
            KernelRpcValueKind.List => KernelRpcValue.ListFromOwnedItems(ReadItems(ref reader, depth)),
            KernelRpcValueKind.Record => KernelRpcValue.RecordFromOwnedFields(ReadItems(ref reader, depth)),
            KernelRpcValueKind.Map => KernelRpcValue.MapFromOwnedEntries(ReadItems(ref reader, depth)),
            _ => default
        };
        return value.Kind != default;
    }

    private static KernelRpcValue ReadBool(ref Reader reader)
    {
        var value = reader.ReadByte();
        return value switch
        {
            0 => KernelRpcValue.Bool(false),
            1 => KernelRpcValue.Bool(true),
            _ => throw new FormatException("Server extension payload contains an invalid bool value.")
        };
    }

    private static KernelRpcValue ReadDouble(ref Reader reader)
    {
        var value = BitConverter.Int64BitsToDouble(reader.ReadInt64());
        if (!double.IsFinite(value))
        {
            throw new FormatException("Server extension payload contains a non-finite F64 value.");
        }

        return KernelRpcValue.Double(value);
    }

    private static KernelRpcValue[] ReadItems(ref Reader reader, int depth)
    {
        var nextDepth = depth + 1;
        if (nextDepth > MaxDecodeDepth)
        {
            throw new FormatException("Server extension payload exceeds the maximum nesting depth.");
        }

        var count = reader.ReadLength();
        reader.ReserveItems(count);
        var values = count == 0 ? Array.Empty<KernelRpcValue>() : new KernelRpcValue[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadValue(ref reader, nextDepth);
        }

        return values;
    }
}
