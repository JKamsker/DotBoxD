namespace DotBoxD.Kernels.Benchmarks.Plugins;

internal static class PluginProbeDispatcher
{
    public static bool TryRun(string[] args)
    {
        if (args.Contains("--probe-remote-result-hook", StringComparer.OrdinalIgnoreCase))
        {
            RemoteResultHookProbe.Run();
            return true;
        }

        if (args.Contains("--probe-subscription-dispatch", StringComparer.OrdinalIgnoreCase))
        {
            SubscriptionDispatchProbe.Run();
            return true;
        }

        if (args.Contains("--probe-hook-dispatch", StringComparer.OrdinalIgnoreCase))
        {
            HookDispatchProbe.Run();
            return true;
        }

        return false;
    }
}
