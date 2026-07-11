using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Model;

using static ResourceMeterMath;

internal static class ResourceFlatValueShapeMeter
{
    private const int MaxUnchargedShapeScanValues = 61;

    public static bool TryMeasure(
        SandboxValue value,
        CancellationToken cancellationToken,
        out ValueShape shape)
    {
        cancellationToken.ThrowIfCancellationRequested();
        shape = new ValueShape(0, 0, 0, 0, 0, 0);
        if (value is ListValue list)
        {
            return TryMeasureList(list, cancellationToken, ref shape);
        }

        if (value is RecordValue record)
        {
            return TryMeasureRecord(record, cancellationToken, ref shape);
        }

        return TryAddScalarShape(value, ref shape);
    }

    private static bool TryMeasureList(
        ListValue list,
        CancellationToken cancellationToken,
        ref ValueShape shape)
    {
        var values = list.Values;
        if (values.Count > MaxUnchargedShapeScanValues)
        {
            return false;
        }

        shape = shape with
        {
            Elements = values.Count,
            MaxListLength = values.Count,
            Depth = 1
        };
        for (var i = 0; i < values.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryAddScalarShape(values[i], ref shape))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMeasureRecord(
        RecordValue record,
        CancellationToken cancellationToken,
        ref ValueShape shape)
    {
        var fields = record.Fields;
        if (fields.Count > MaxUnchargedShapeScanValues)
        {
            return false;
        }

        shape = shape with
        {
            Elements = fields.Count,
            MaxListLength = fields.Count,
            Depth = 1
        };
        for (var i = 0; i < fields.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryAddScalarShape(fields[i], ref shape))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAddScalarShape(SandboxValue value, ref ValueShape shape)
    {
        switch (value)
        {
            case UnitValue or BoolValue or I32Value or I64Value or F64Value or GuidValue:
                return true;
            case StringValue text:
                AddTextShape(ref shape, SandboxLiteralConstraints.TextShape(text.Value));
                return true;
            case OpaqueIdValue id:
                AddTextShape(ref shape, SandboxLiteralConstraints.TextShape(id.Value));
                return true;
            case SandboxPathValue path:
                AddTextShape(ref shape, SandboxLiteralConstraints.TextShape(path.Value?.RelativePath));
                return true;
            case SandboxUriValue uri:
                AddTextShape(ref shape, SandboxLiteralConstraints.TextShape(uri.Value?.Value));
                return true;
            default:
                return false;
        }
    }

    private static void AddTextShape(ref ValueShape shape, ValueShape text)
    {
        shape = shape with
        {
            MaxStringLength = Math.Max(shape.MaxStringLength, text.MaxStringLength),
            StringBytes = AddChecked(shape.StringBytes, text.StringBytes, "string byte budget exhausted")
        };
    }
}
