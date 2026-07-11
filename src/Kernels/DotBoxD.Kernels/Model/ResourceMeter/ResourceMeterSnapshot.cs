using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Model;

internal static class ResourceMeterSnapshot
{
    public static SandboxResourceUsage Create(ResourceMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        return new SandboxResourceUsage(
            meter.FuelUsed,
            meter.Limits.MaxFuel,
            meter.LoopIterations,
            meter.AllocatedBytes,
            meter.HostCalls,
            meter.FileBytesRead,
            meter.FileBytesWritten,
            meter.NetworkBytesRead,
            meter.NetworkBytesWritten,
            meter.LogEvents,
            meter.CollectionElements,
            meter.StringBytes);
    }
}
