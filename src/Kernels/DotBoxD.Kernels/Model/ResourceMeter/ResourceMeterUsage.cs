using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Model;

using static ResourceMeterMath;

internal readonly record struct ResourceLoopCharge(long LoopIterations, long Fuel);

internal readonly record struct ResourceUsageCharge(
    long CollectionElements,
    long AllocatedBytes,
    long StringBytes);

internal static class ResourceMeterUsageCharges
{
    public static long AddSingleLoopIteration(
        long currentLoopIterations,
        long fuelAmount)
    {
        if (fuelAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fuelAmount));
        }

        return AddNonNegativeChecked(
            currentLoopIterations,
            1,
            "loop iteration budget exhausted");
    }

    public static ResourceLoopCharge ChargeLoopIterations(
        long currentLoopIterations,
        ResourceLimits limits,
        long iterations,
        long fuelPerIteration)
    {
        if (iterations < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        if (fuelPerIteration <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fuelPerIteration));
        }

        if (iterations == 0)
        {
            return new ResourceLoopCharge(currentLoopIterations, 0);
        }

        return new ResourceLoopCharge(
            AddChecked(currentLoopIterations, iterations, "loop iteration budget exhausted"),
            MultiplyChecked(iterations, fuelPerIteration, "fuel exhausted"));
    }

    public static bool CanChargeLoopIterations(
        long currentLoopIterations,
        long currentFuel,
        ResourceLimits limits,
        long iterations,
        long fuelPerIteration)
    {
        if (iterations < 0 || fuelPerIteration <= 0)
        {
            return false;
        }

        try
        {
            var fuel = MultiplyChecked(iterations, fuelPerIteration, "fuel exhausted");
            return currentLoopIterations <= limits.MaxLoopIterations - iterations &&
                   fuel >= 0 &&
                   currentFuel <= limits.MaxFuel - fuel;
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    public static long AddBytes(long current, long bytes, string quotaMessage)
    {
        ThrowIfNegative(bytes, nameof(bytes));
        return AddChecked(current, bytes, quotaMessage);
    }

    public static ValueShape GetStringAllocationShape(int charLength)
    {
        if (charLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(charLength));
        }

        var bytes = SandboxLiteralConstraints.StringByteCount(charLength);
        return new ValueShape(0, 0, 0, 0, charLength, bytes);
    }

    public static bool CanChargeStringValues(
        string value,
        long count,
        ResourceLimits limits,
        long allocatedBytes,
        long stringBytes)
    {
        if (count < 0 || value.Length > limits.MaxStringLength)
        {
            return false;
        }

        try
        {
            var bytes = MultiplyChecked(
                SandboxLiteralConstraints.StringByteCount(value.Length),
                count,
                "string byte budget exhausted");
            return allocatedBytes <= limits.MaxAllocatedBytes - bytes &&
                   stringBytes <= limits.MaxTotalStringBytes - bytes;
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    public static ResourceUsageCharge ChargeStringValues(
        string value,
        long count,
        ResourceLimits limits,
        long allocatedBytes,
        long stringBytes)
    {
        if (count == 0)
        {
            return new ResourceUsageCharge(0, allocatedBytes, stringBytes);
        }

        if (!CanChargeStringValues(value, count, limits, allocatedBytes, stringBytes))
        {
            throw Quota(value.Length > limits.MaxStringLength
                ? "string length budget exhausted"
                : "string byte budget exhausted");
        }

        var bytes = MultiplyChecked(
            SandboxLiteralConstraints.StringByteCount(value.Length),
            count,
            "string byte budget exhausted");
        return new ResourceUsageCharge(
            0,
            AddChecked(allocatedBytes, bytes, "allocation budget exhausted"),
            AddChecked(stringBytes, bytes, "string byte budget exhausted"));
    }

    public static bool CanChargeHostCalls(int hostCalls, ResourceLimits limits, long calls)
        => calls >= 0 &&
           calls <= int.MaxValue &&
           hostCalls <= limits.MaxHostCalls - calls;

    public static int AddLogEvent(int logEvents, string message, ResourceLimits limits)
    {
        if (message.Length > limits.MaxLogMessageLength)
        {
            throw Quota("log message length budget exhausted");
        }

        return AddChecked(logEvents, 1, "log event budget exhausted");
    }

    public static void ValidateCollectionShape(ValueShape shape, ResourceLimits limits)
    {
        if (shape.MaxListLength > limits.MaxListLength)
        {
            throw Quota("list length budget exhausted");
        }

        if (shape.MaxMapEntries > limits.MaxMapEntries)
        {
            throw Quota("map entry budget exhausted");
        }

        if (shape.Depth > limits.MaxCollectionDepth)
        {
            throw Quota("collection depth budget exhausted");
        }
    }

    public static void ValidateStringShape(ValueShape shape, ResourceLimits limits)
    {
        if (shape.MaxStringLength > limits.MaxStringLength)
        {
            throw Quota("string length budget exhausted");
        }

    }
}
