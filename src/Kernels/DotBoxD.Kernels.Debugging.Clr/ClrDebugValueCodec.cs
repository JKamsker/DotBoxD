using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Debugging.Clr;

internal sealed record ClrDebugValue
{
    public required string Kind { get; init; }

    public bool? Bool { get; init; }

    public int? I32 { get; init; }

    public long? I64 { get; init; }

    public double? F64 { get; init; }

    public string? Text { get; init; }

    public ClrDebugValue[]? Items { get; init; }

    public ClrDebugMapEntry[]? Entries { get; init; }

    public SandboxType? MapKeyType { get; init; }

    public SandboxType? MapValueType { get; init; }

    public static ClrDebugValue FromSandbox(SandboxValue value) => value switch
    {
        UnitValue => new ClrDebugValue { Kind = "unit" },
        BoolValue item => new ClrDebugValue { Kind = "bool", Bool = item.Value },
        I32Value item => new ClrDebugValue { Kind = "i32", I32 = item.Value },
        I64Value item => new ClrDebugValue { Kind = "i64", I64 = item.Value },
        F64Value item => new ClrDebugValue { Kind = "f64", F64 = item.Value },
        StringValue item => new ClrDebugValue { Kind = "string", Text = item.Value },
        _ => FromStructuredSandbox(value)
    };

    private static ClrDebugValue FromStructuredSandbox(SandboxValue value) => value switch
    {
        GuidValue item => new ClrDebugValue { Kind = "guid", Text = item.Value.ToString("D") },
        OpaqueIdValue item => new ClrDebugValue { Kind = "string", Text = item.Value },
        SandboxPathValue item => new ClrDebugValue { Kind = "string", Text = item.Value.RelativePath },
        SandboxUriValue item => new ClrDebugValue { Kind = "string", Text = item.Value.Value },
        ListValue item => new ClrDebugValue
        {
            Kind = "list",
            Items = item.Values.Select(FromSandbox).ToArray()
        },
        RecordValue item => new ClrDebugValue
        {
            Kind = "record",
            Items = item.Fields.Select(FromSandbox).ToArray()
        },
        MapValue item => new ClrDebugValue
        {
            Kind = "map",
            MapKeyType = item.KeyType,
            MapValueType = item.ValueType,
            Entries = item.Values
                .Select(entry => new ClrDebugMapEntry(
                    FromSandbox(entry.Key),
                    FromSandbox(entry.Value)))
                .ToArray()
        },
        _ => throw new NotSupportedException(
            $"Trusted CLR evaluation cannot serialize sandbox value '{value.GetType().Name}'.")
    };

    public object? ToClr() => Kind switch
    {
        "unit" => null,
        "bool" => Bool,
        "i32" => I32,
        "i64" => I64,
        "f64" => F64,
        "string" => Text,
        _ => ToStructuredClr()
    };

    private object? ToStructuredClr() => Kind switch
    {
        "guid" => Guid.Parse(Text!),
        "list" or "record" => (Items ?? []).Select(item => item.ToClr()).ToArray(),
        "map" => (Entries ?? []).ToDictionary(
            entry => entry.Key.ToClr()
                ?? throw new InvalidOperationException("CLR evaluation cannot represent a null map key."),
            entry => entry.Value.ToClr()),
        _ => throw new InvalidOperationException($"Unsupported serialized debug value kind '{Kind}'.")
    };

    public SandboxValue ToSandbox() => Kind switch
    {
        "unit" => SandboxValue.Unit,
        "bool" => SandboxValue.FromBool(Bool!.Value),
        "i32" => SandboxValue.FromInt32(I32!.Value),
        "i64" => SandboxValue.FromInt64(I64!.Value),
        "f64" => SandboxValue.FromDouble(F64!.Value),
        "string" => SandboxValue.FromString(Text!),
        _ => ToStructuredSandbox()
    };

    private SandboxValue ToStructuredSandbox() => Kind switch
    {
        "guid" => SandboxValue.FromGuid(Guid.Parse(Text!)),
        "list" => SandboxValue.FromList((Items ?? []).Select(item => item.ToSandbox()).ToArray()),
        "record" => SandboxValue.FromRecord((Items ?? []).Select(item => item.ToSandbox()).ToArray()),
        "map" => SandboxValue.FromMap(
            (Entries ?? []).ToDictionary(entry => entry.Key.ToSandbox(), entry => entry.Value.ToSandbox()),
            MapKeyType ?? throw new InvalidOperationException("Serialized debug map key type is missing."),
            MapValueType ?? throw new InvalidOperationException("Serialized debug map value type is missing.")),
        _ => throw new InvalidOperationException($"Unsupported serialized debug value kind '{Kind}'.")
    };

    public static ClrDebugValue FromClr(object? value) => value switch
    {
        null => new ClrDebugValue { Kind = "unit" },
        SandboxValue sandbox => FromSandbox(sandbox),
        bool item => new ClrDebugValue { Kind = "bool", Bool = item },
        byte item => new ClrDebugValue { Kind = "i32", I32 = item },
        short item => new ClrDebugValue { Kind = "i32", I32 = item },
        int item => new ClrDebugValue { Kind = "i32", I32 = item },
        long item => new ClrDebugValue { Kind = "i64", I64 = item },
        _ => FromExtendedClr(value)
    };

    private static ClrDebugValue FromExtendedClr(object value) => value switch
    {
        float item => Finite(item),
        double item => Finite(item),
        string item => new ClrDebugValue { Kind = "string", Text = item },
        Guid item => new ClrDebugValue { Kind = "guid", Text = item.ToString("D") },
        System.Collections.IEnumerable items when value is not string => new ClrDebugValue
        {
            Kind = "list",
            Items = items.Cast<object?>().Select(FromClr).ToArray()
        },
        _ => throw new InvalidOperationException(
            $"CLR evaluation result '{value.GetType().FullName}' cannot cross the sandbox value boundary.")
    };

    private static ClrDebugValue Finite(double value)
        => double.IsFinite(value)
            ? new ClrDebugValue { Kind = "f64", F64 = value }
            : throw new InvalidOperationException("CLR evaluation returned a non-finite floating-point value.");
}

internal sealed record ClrDebugMapEntry(ClrDebugValue Key, ClrDebugValue Value);
