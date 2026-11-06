using System.Reflection;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Lifecycle;

// A same-id reinstall replaces (and revokes) the previous kernel; the session must stop tracking the replaced
// install id instead of accumulating one entry per hot-replace for its whole lifetime.
public sealed class PluginSessionReinstallTrackingTests
{
    [Fact]
    public async Task Repeated_same_id_install_tracks_only_the_live_install()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        using var session = server.CreateSession();

        for (var i = 0; i < 5; i++)
        {
            await session.InstallAsync(FireDamagePluginPackage.Create());
        }

        var current = server.Kernels.Get("fire-damage");
        Assert.Equal([current.InstallId], OwnedInstallIds(session));
        Assert.True(session.Owns("fire-damage"));
    }

    [Fact]
    public async Task Repeated_install_and_wire_tracks_only_the_live_install()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        using var session = server.CreateSession();

        for (var i = 0; i < 5; i++)
        {
            await session.InstallAndWireAsync(FireDamagePluginPackage.Create(), static _ => { });
        }

        var current = server.Kernels.Get("fire-damage");
        Assert.Equal([current.InstallId], OwnedInstallIds(session));
        Assert.True(session.Owns("fire-damage"));
    }

    private static string[] OwnedInstallIds(PluginSession session)
        => [.. (HashSet<string>)typeof(PluginSession)
            .GetField("_ownedInstallIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session)!];
}
