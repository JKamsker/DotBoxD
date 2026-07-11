using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox.Values;

internal static class SandboxValidatedValueShapeLimits
{
    public static ValueShape AddCollection(
        ValueShape shape,
        int elements,
        int listLength,
        int mapEntries,
        int depth,
        ResourceLimits? limits)
    {
        var totalElements = AddLong(shape.Elements, elements, "collection element budget exhausted");
        if (limits is not null && totalElements > limits.MaxTotalCollectionElements)
        {
            throw SandboxValidatedValueShapeErrors.Quota("collection element budget exhausted");
        }

        return shape with
        {
            Elements = totalElements,
            MaxListLength = Math.Max(shape.MaxListLength, listLength),
            MaxMapEntries = Math.Max(shape.MaxMapEntries, mapEntries),
            Depth = Math.Max(shape.Depth, depth)
        };
    }

    public static ValueShape AddText(ValueShape shape, ValueShape text, ResourceLimits? limits)
    {
        if (limits is not null && text.MaxStringLength > limits.MaxStringLength)
        {
            throw SandboxValidatedValueShapeErrors.Quota("string length budget exhausted");
        }

        var stringBytes = AddLong(shape.StringBytes, text.StringBytes, "string byte budget exhausted");
        if (limits is not null && stringBytes > limits.MaxTotalStringBytes)
        {
            throw SandboxValidatedValueShapeErrors.Quota("string byte budget exhausted");
        }

        return shape with
        {
            MaxStringLength = Math.Max(shape.MaxStringLength, text.MaxStringLength),
            StringBytes = stringBytes
        };
    }

    public static void EnsureCollectionLimits(int listLength, int mapEntries, int depth, ResourceLimits? limits)
    {
        if (limits is null)
        {
            return;
        }

        if (listLength > limits.MaxListLength)
        {
            throw SandboxValidatedValueShapeErrors.Quota("list length budget exhausted");
        }

        if (mapEntries > limits.MaxMapEntries)
        {
            throw SandboxValidatedValueShapeErrors.Quota("map entry budget exhausted");
        }

        if (depth > limits.MaxCollectionDepth)
        {
            throw SandboxValidatedValueShapeErrors.Quota("collection depth budget exhausted");
        }
    }

    private static long AddLong(long current, long amount, string quotaMessage)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw SandboxValidatedValueShapeErrors.Quota(quotaMessage);
        }
    }
}
