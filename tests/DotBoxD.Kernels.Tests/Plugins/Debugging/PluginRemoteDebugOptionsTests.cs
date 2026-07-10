using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging;

public sealed class PluginRemoteDebugOptionsTests
{
    [Fact]
    public void Defaults_are_disabled_server_scoped_and_lease_stops_for_five_minutes()
    {
        var options = new PluginRemoteDebugOptions();

        options.Validate();
        Assert.False(options.Enabled);
        Assert.Equal(KernelDebugPauseScope.Server, options.DefaultPauseScope);
        Assert.Equal([KernelDebugPauseScope.Server], options.AllowedPauseScopes);
        Assert.Equal(TimeSpan.FromMinutes(5), options.StopLease);
    }

    [Fact]
    public void Default_scope_must_be_host_allowed()
    {
        var options = new PluginRemoteDebugOptions
        {
            DefaultPauseScope = KernelDebugPauseScope.Execution,
            AllowedPauseScopes = [KernelDebugPauseScope.Server]
        };

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Limits_must_be_positive(int limit)
    {
        var options = new PluginRemoteDebugOptions { MaxMessageBytes = limit };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }
}
