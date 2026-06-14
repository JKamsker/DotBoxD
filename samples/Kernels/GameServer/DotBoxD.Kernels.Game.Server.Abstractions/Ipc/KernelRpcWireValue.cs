namespace DotBoxD.Kernels.Game.Server.Abstractions;

using MessagePack;

/// <summary>
/// Transport-neutral value used by the GameServer sample to forward plugin-defined kernel RPC service
/// arguments and results over the ordinary IPC control plane. The server converts it to the installed
/// kernel's declared sandbox parameter types before invoking verified IR.
/// </summary>
[MessagePackObject]
public readonly struct KernelRpcWireValue
{
    public const string UnitKind = "unit";
    public const string BoolKind = "bool";
    public const string Int32Kind = "i32";
    public const string Int64Kind = "i64";
    public const string DoubleKind = "f64";
    public const string StringKind = "string";
    public const string ListKind = "list";
    public const string RecordKind = "record";

    [SerializationConstructor]
    public KernelRpcWireValue(
        string kind,
        bool boolValue,
        int int32Value,
        long int64Value,
        double doubleValue,
        string stringValue,
        KernelRpcWireValue[] items)
    {
        Kind = kind ?? string.Empty;
        BoolValue = boolValue;
        Int32Value = int32Value;
        Int64Value = int64Value;
        DoubleValue = doubleValue;
        StringValue = stringValue ?? string.Empty;
        Items = items ?? [];
    }

    [Key(0)]
    public string Kind { get; }

    [Key(1)]
    public bool BoolValue { get; }

    [Key(2)]
    public int Int32Value { get; }

    [Key(3)]
    public long Int64Value { get; }

    [Key(4)]
    public double DoubleValue { get; }

    [Key(5)]
    public string StringValue { get; }

    [Key(6)]
    public KernelRpcWireValue[] Items { get; }

    public static KernelRpcWireValue Unit()
        => new(UnitKind, false, 0, 0L, 0D, string.Empty, []);

    public static KernelRpcWireValue Bool(bool value)
        => new(BoolKind, value, 0, 0L, 0D, string.Empty, []);

    public static KernelRpcWireValue Int32(int value)
        => new(Int32Kind, false, value, 0L, 0D, string.Empty, []);

    public static KernelRpcWireValue Int64(long value)
        => new(Int64Kind, false, 0, value, 0D, string.Empty, []);

    public static KernelRpcWireValue Double(double value)
        => new(DoubleKind, false, 0, 0L, value, string.Empty, []);

    public static KernelRpcWireValue String(string value)
        => new(StringKind, false, 0, 0L, 0D, value, []);

    public static KernelRpcWireValue List(KernelRpcWireValue[] items)
        => new(ListKind, false, 0, 0L, 0D, string.Empty, items);

    public static KernelRpcWireValue Record(KernelRpcWireValue[] fields)
        => new(RecordKind, false, 0, 0L, 0D, string.Empty, fields);
}

public static class KernelRpcWireValueConverter
{
    public static KernelRpcWireValue FromSandboxValue(SandboxValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            UnitValue => KernelRpcWireValue.Unit(),
            BoolValue boolean => KernelRpcWireValue.Bool(boolean.Value),
            I32Value number => KernelRpcWireValue.Int32(number.Value),
            I64Value number => KernelRpcWireValue.Int64(number.Value),
            F64Value number => KernelRpcWireValue.Double(number.Value),
            StringValue text => KernelRpcWireValue.String(text.Value),
            ListValue list => KernelRpcWireValue.List(ConvertList(list.Values)),
            RecordValue record => KernelRpcWireValue.Record(ConvertList(record.Fields)),
            _ => throw new NotSupportedException(
                $"Kernel RPC IPC cannot marshal sandbox value '{value.GetType().Name}'.")
        };
    }

    public static SandboxValue ToSandboxValue(KernelRpcWireValue value, SandboxType expectedType)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        if (expectedType.Equals(SandboxType.Unit))
        {
            RequireKind(value, KernelRpcWireValue.UnitKind, expectedType);
            return SandboxValue.Unit;
        }

        if (expectedType.Equals(SandboxType.Bool))
        {
            RequireKind(value, KernelRpcWireValue.BoolKind, expectedType);
            return SandboxValue.FromBool(value.BoolValue);
        }

        if (expectedType.Equals(SandboxType.I32))
        {
            RequireKind(value, KernelRpcWireValue.Int32Kind, expectedType);
            return SandboxValue.FromInt32(value.Int32Value);
        }

        if (expectedType.Equals(SandboxType.I64))
        {
            RequireKind(value, KernelRpcWireValue.Int64Kind, expectedType);
            return SandboxValue.FromInt64(value.Int64Value);
        }

        if (expectedType.Equals(SandboxType.F64))
        {
            RequireKind(value, KernelRpcWireValue.DoubleKind, expectedType);
            return SandboxValue.FromDouble(value.DoubleValue);
        }

        if (expectedType.Equals(SandboxType.String))
        {
            RequireKind(value, KernelRpcWireValue.StringKind, expectedType);
            return SandboxValue.FromString(value.StringValue);
        }

        if (expectedType.Name == "List" && expectedType.Arguments.Count == 1)
        {
            RequireKind(value, KernelRpcWireValue.ListKind, expectedType);
            var itemType = expectedType.Arguments[0];
            var items = new SandboxValue[value.Items.Length];
            for (var i = 0; i < value.Items.Length; i++)
            {
                items[i] = ToSandboxValue(value.Items[i], itemType);
            }

            return SandboxValue.FromList(items, itemType);
        }

        if (expectedType.IsRecord)
        {
            RequireKind(value, KernelRpcWireValue.RecordKind, expectedType);
            if (value.Items.Length != expectedType.Arguments.Count)
            {
                throw new NotSupportedException(
                    $"Kernel RPC IPC record expected {expectedType.Arguments.Count} field(s) but received {value.Items.Length}.");
            }

            var fields = new SandboxValue[value.Items.Length];
            for (var i = 0; i < value.Items.Length; i++)
            {
                fields[i] = ToSandboxValue(value.Items[i], expectedType.Arguments[i]);
            }

            return SandboxValue.FromRecord(fields);
        }

        throw new NotSupportedException($"Kernel RPC IPC cannot marshal expected sandbox type '{expectedType}'.");
    }

    private static KernelRpcWireValue[] ConvertList(IReadOnlyList<SandboxValue> values)
    {
        var converted = new KernelRpcWireValue[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            converted[i] = FromSandboxValue(values[i]);
        }

        return converted;
    }

    private static void RequireKind(KernelRpcWireValue value, string expectedKind, SandboxType expectedType)
    {
        if (!string.Equals(value.Kind, expectedKind, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Kernel RPC IPC expected wire kind '{expectedKind}' for '{expectedType}' but received '{value.Kind}'.");
        }
    }
}
