namespace DotBoxD.Kernels.Game.Plugin;

internal static class PluginLaunchMode
{
    private const string SkipAdvancedUsageVariable = "DOTBOXD_E2E_SKIP_ADVANCED_USAGE";

    internal static bool RunAdvancedUsage
        => ShouldRunAdvancedUsage(Environment.GetEnvironmentVariable(SkipAdvancedUsageVariable));

    internal static bool ShouldRunAdvancedUsage(string? skipValue)
        => !string.Equals(skipValue, "1", StringComparison.Ordinal);
}
