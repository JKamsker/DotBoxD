namespace DotBoxD.Kernels.Benchmarks.PluginAnalyzer;

internal static class PluginAnalyzerProbeDispatcher
{
    public static bool TryRun(string[] args)
    {
        if (args.Contains("--probe-hook-chain-discovery", StringComparer.OrdinalIgnoreCase))
        {
            HookChainDiscoveryProbe.Run();
            return true;
        }

        if (args.Contains("--probe-plugin-package-collision-discovery", StringComparer.OrdinalIgnoreCase))
        {
            PluginPackageCollisionDiscoveryProbe.Run();
            return true;
        }

        if (args.Contains("--probe-server-extension-request-helpers", StringComparer.OrdinalIgnoreCase))
        {
            ServerExtensionRequestHelperProbe.Run();
            return true;
        }

        if (args.Contains("--probe-generic-construction-reachability", StringComparer.OrdinalIgnoreCase))
        {
            GenericConstructionReachabilityProbe.Run();
            return true;
        }

        if (args.Contains("--probe-invokeasync-resolution", StringComparer.OrdinalIgnoreCase))
        {
            InvokeAsyncResolutionProbe.Run();
            return true;
        }

        return false;
    }
}
