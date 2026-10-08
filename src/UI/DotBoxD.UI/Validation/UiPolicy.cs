using System.Collections.Immutable;

namespace DotBoxD.UI;

/// <summary>Host-selected structural limits, independent of sandbox kernel fuel and quotas.</summary>
public sealed record UiPolicy
{
    public ImmutableArray<UiPrimitive> AllowedPrimitives { get; init; } = [.. Enum.GetValues<UiPrimitive>()];
    public int MaxPackageBytes { get; init; } = 2 * 1024 * 1024;
    public ImmutableArray<string> AllowedExtensionSchemas { get; init; } = [];
    public int MaxExtensionBytes { get; init; } = 16 * 1024;
    public int MaxResources { get; init; } = 16;
    public int MaxResourceBytes { get; init; } = 1024 * 1024;
    public int MaxTotalResourceBytes { get; init; } = 4 * 1024 * 1024;
    public int MaxImagePixels { get; init; } = 262_144;
    public int MaxNodes { get; init; } = 2_000;
    public int MaxDepth { get; init; } = 64;
    public int MaxChildren { get; init; } = 1_000;
    public int MaxItems { get; init; } = 1_000;
    public int MaxInputEventsPerSecond { get; init; } = 1_000;
    public int MaxStateSlots { get; init; } = 256;
    public int MaxStateBytes { get; init; } = 1024 * 1024;
    public int MaxStringLength { get; init; } = 16_384;
    public int MaxKernels { get; init; } = 128;
    public int MaxKernelBytes { get; init; } = 256 * 1024;
    public int MaxEvents { get; init; } = 256;
    public int MaxPatchSlots { get; init; } = 128;
    public int MaxInFlightRemoteEvents { get; init; } = 4;
    /// <summary>
    /// Deadline for a remote dispatch. Exceeding it disconnects/releases the session so an
    /// unresponsive transport cannot accumulate timed-out work. Caller cancellation affects one call.
    /// </summary>
    public TimeSpan RemoteEventTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public void Validate()
    {
        foreach (var limit in new[] { MaxPackageBytes, MaxNodes, MaxDepth, MaxChildren, MaxStateSlots,
            MaxStateBytes, MaxStringLength, MaxItems, MaxInputEventsPerSecond, MaxKernels, MaxKernelBytes, MaxEvents, MaxPatchSlots, MaxInFlightRemoteEvents })
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(MaxExtensionBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxResources);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxResourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalResourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxImagePixels);

        if (AllowedExtensionSchemas.IsDefault || AllowedExtensionSchemas.Any(s => !UiResourceValidator.ValidHandle(s)) ||
            AllowedPrimitives.IsDefault || AllowedPrimitives.Any(p => !Enum.IsDefined(p)) ||
            MaxDepth > 256 || RemoteEventTimeout <= TimeSpan.Zero ||
            RemoteEventTimeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentException("UI policy limits must be positive and supported.");
        }
    }
}

public sealed class UiValidationException(string message) : Exception(message);
