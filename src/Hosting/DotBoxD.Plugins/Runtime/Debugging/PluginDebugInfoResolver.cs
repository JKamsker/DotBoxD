using DotBoxD.Kernels.Debugging;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Debugging;

internal static class PluginDebugInfoResolver
{
    public static KernelDebugInfo? Resolve(
        object gate,
        IEnumerable<InstalledKernel> kernels,
        PluginDebugSession session,
        string pluginId)
    {
        lock (gate)
        {
            return kernels.FirstOrDefault(candidate =>
                ReferenceEquals(candidate.OwnerId, session.Owner) &&
                string.Equals(candidate.Manifest.PluginId, pluginId, StringComparison.Ordinal))?.Package.DebugInfo;
        }
    }
}
