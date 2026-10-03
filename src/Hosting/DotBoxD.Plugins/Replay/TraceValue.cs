using System.Globalization;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Plugins.Replay;

/// <summary>Portable sandbox value; map items alternate keys and values. No CLR type names are loaded.</summary>
public sealed record TraceValue(SandboxType Type, string? Text, IReadOnlyList<TraceValue> Items)
{
    private readonly IReadOnlyList<TraceValue> _items = Array.AsReadOnly(Items.ToArray());
    public IReadOnlyList<TraceValue> Items { get => _items; init => _items = Array.AsReadOnly(value.ToArray()); }

    public static TraceValue Capture(SandboxValue value) => Capture(value, 0);

    private static TraceValue Capture(SandboxValue value, int depth)
    {
        if (depth > 32)
        {
            throw new InvalidDataException("Trace values may nest at most 32 levels.");
        }
        return CaptureScalar(value) ?? CaptureComposite(value, depth);
    }

    private static TraceValue? CaptureScalar(SandboxValue value) => value switch
    {
        UnitValue => new(value.Type, null, []),
        BoolValue item => new(value.Type, item.Value ? "true" : "false", []),
        I32Value item => new(value.Type, item.Value.ToString(CultureInfo.InvariantCulture), []),
        I64Value item => new(value.Type, item.Value.ToString(CultureInfo.InvariantCulture), []),
        F64Value item => new(value.Type, item.Value.ToString("R", CultureInfo.InvariantCulture), []),
        StringValue item => new(value.Type, item.Value, []),
        GuidValue item => new(value.Type, item.Value.ToString("D"), []),
        _ => null
    };

    private static TraceValue CaptureComposite(SandboxValue value, int depth)
    {
        return value switch
        {
            SandboxPathValue item => new(value.Type, item.Value.RelativePath, []),
            SandboxUriValue item => new(value.Type, item.Value.Value, []),
            OpaqueIdValue item => new(value.Type, item.Value, []),
            ListValue item => new(value.Type, null, item.Values.Select(v => Capture(v, depth + 1)).ToArray()),
            RecordValue item => new(value.Type, null, item.Fields.Select(v => Capture(v, depth + 1)).ToArray()),
            MapValue item => new(value.Type, null, item.Values.SelectMany(pair =>
                new[] { Capture(pair.Key, depth + 1), Capture(pair.Value, depth + 1) }).ToArray()),
            _ => throw new InvalidDataException("Unsupported sandbox trace value.")
        };
    }

    public SandboxValue Restore() => Restore(0);

    private SandboxValue Restore(int depth)
    {
        if (depth > 32 || Items.Count > 100_000)
        {
            throw new InvalidDataException("Trace value exceeds collection limits.");
        }
        var result = RestoreScalar() ?? RestoreComposite(depth);
        if (result.Type != Type || (Type.Arguments.Count == 0 && Items.Count != 0) ||
            (Type.Arguments.Count != 0 && Text is not null))
        {
            throw new InvalidDataException("Trace value shape does not match its type.");
        }
        return result;
    }

    private SandboxValue? RestoreScalar() => Type.Name switch
    {
        "Unit" => SandboxValue.Unit,
        "Bool" => SandboxValue.FromBool(bool.Parse(RequiredText())),
        "I32" => SandboxValue.FromInt32(int.Parse(RequiredText(), CultureInfo.InvariantCulture)),
        "I64" => SandboxValue.FromInt64(long.Parse(RequiredText(), CultureInfo.InvariantCulture)),
        "F64" => SandboxValue.FromDouble(double.Parse(RequiredText(), CultureInfo.InvariantCulture)),
        "String" => SandboxValue.FromString(RequiredText()),
        "Guid" => SandboxValue.FromGuid(Guid.Parse(RequiredText())),
        _ => null
    };

    private SandboxValue RestoreComposite(int depth) => Type.Name switch
    {
        "SandboxPath" => SandboxValue.FromPath(RequiredText()),
        "SandboxUri" => SandboxValue.FromUri(RequiredText()),
        "List" => RestoreList(depth),
        "Record" => SandboxValue.FromRecord(Items.Select(item => item.Restore(depth + 1)).ToArray()),
        "Map" => RestoreMap(depth),
        _ when Type.Arguments.Count == 0 => SandboxValue.FromOpaqueId(Type.Name, RequiredText()),
        _ => throw new InvalidDataException("Unsupported trace value type.")
    };

    private SandboxValue RestoreList(int depth)
    {
        if (Type.Arguments.Count != 1)
        {
            throw new InvalidDataException("Trace list requires an item type.");
        }
        return SandboxValue.FromList(Items.Select(item => item.Restore(depth + 1)).ToArray(), Type.Arguments[0]);
    }

    private SandboxValue RestoreMap(int depth)
    {
        if (Type.Arguments.Count != 2 || Items.Count % 2 != 0)
        {
            throw new InvalidDataException("Trace map requires key/value types and paired items.");
        }
        var values = new Dictionary<SandboxValue, SandboxValue>();
        for (var index = 0; index < Items.Count; index += 2)
        {
            values.Add(Items[index].Restore(depth + 1), Items[index + 1].Restore(depth + 1));
        }
        return SandboxValue.FromMap(values, Type.Arguments[0], Type.Arguments[1]);
    }

    private string RequiredText() => Text ?? throw new InvalidDataException("Trace value requires text.");
}
