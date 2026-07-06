namespace DotBoxD.Kernels.Model;

public static class ResourceLimitValidation
{
    private static readonly TimeSpan MaxSupportedWallTime = TimeSpan.FromMilliseconds(int.MaxValue);

    public static void Validate(ResourceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ThrowIfNegative(limits.MaxFuel, nameof(ResourceLimits.MaxFuel));
        ThrowIfNegative(limits.MaxLoopIterations, nameof(ResourceLimits.MaxLoopIterations));
        ThrowIfNegative(limits.MaxAllocatedBytes, nameof(ResourceLimits.MaxAllocatedBytes));
        ThrowIfNegative(limits.MaxCallDepth, nameof(ResourceLimits.MaxCallDepth));
        ThrowIfNegative(limits.MaxHostCalls, nameof(ResourceLimits.MaxHostCalls));
        ThrowIfNegative(limits.MaxListLength, nameof(ResourceLimits.MaxListLength));
        ThrowIfNegative(limits.MaxMapEntries, nameof(ResourceLimits.MaxMapEntries));
        ThrowIfNegative(limits.MaxCollectionDepth, nameof(ResourceLimits.MaxCollectionDepth));
        ThrowIfNegative(limits.MaxTotalCollectionElements, nameof(ResourceLimits.MaxTotalCollectionElements));
        ThrowIfNegative(limits.MaxFileBytesRead, nameof(ResourceLimits.MaxFileBytesRead));
        ThrowIfNegative(limits.MaxFileBytesWritten, nameof(ResourceLimits.MaxFileBytesWritten));
        ThrowIfNegative(limits.MaxNetworkBytesRead, nameof(ResourceLimits.MaxNetworkBytesRead));
        ThrowIfNegative(limits.MaxNetworkBytesWritten, nameof(ResourceLimits.MaxNetworkBytesWritten));
        ThrowIfNegative(limits.MaxLogEvents, nameof(ResourceLimits.MaxLogEvents));
        ThrowIfNegative(limits.MaxLogMessageLength, nameof(ResourceLimits.MaxLogMessageLength));
        ThrowIfNegative(limits.MaxStringLength, nameof(ResourceLimits.MaxStringLength));
        ThrowIfNegative(limits.MaxTotalStringBytes, nameof(ResourceLimits.MaxTotalStringBytes));
        if (limits.MaxWallTime is { } wallTime)
        {
            if (wallTime < TimeSpan.Zero)
            {
                throw ResourceLimitValidationReasons.NonNegative(nameof(ResourceLimits.MaxWallTime));
            }

            if (wallTime > MaxSupportedWallTime)
            {
                throw ResourceLimitValidationReasons.OutOfRange(
                    nameof(ResourceLimits.MaxWallTime),
                    $"must be within the supported range from {TimeSpan.Zero:c} through {MaxSupportedWallTime:c}");
            }
        }
    }

    private static void ThrowIfNegative(long value, string paramName)
    {
        if (value < 0)
        {
            throw ResourceLimitValidationReasons.NonNegative(paramName);
        }
    }
}

internal static class ResourceLimitValidationReasons
{
    private const string ReasonKey = "DotBoxD.Kernels.Model.ResourceLimitValidationReason";

    public static string? GetReason(ArgumentOutOfRangeException exception)
        => exception.Data[ReasonKey] as string;

    public static ArgumentOutOfRangeException NonNegative(string paramName)
        => OutOfRange(paramName, "must be non-negative");

    public static ArgumentOutOfRangeException OutOfRange(string paramName, string reason)
    {
        var exception = new ArgumentOutOfRangeException(paramName, reason);
        exception.Data[ReasonKey] = reason;
        return exception;
    }
}
