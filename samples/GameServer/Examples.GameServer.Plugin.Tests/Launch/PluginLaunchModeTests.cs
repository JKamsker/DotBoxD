namespace DotBoxD.Kernels.Game.Plugin.Tests;

public sealed class PluginLaunchModeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    public void Advanced_usage_runs_by_default(string? skipValue)
    {
        Assert.True(PluginLaunchMode.ShouldRunAdvancedUsage(skipValue));
    }

    [Fact]
    public void Dedicated_e2e_launch_can_skip_unrelated_advanced_usage()
    {
        Assert.False(PluginLaunchMode.ShouldRunAdvancedUsage("1"));
    }
}
