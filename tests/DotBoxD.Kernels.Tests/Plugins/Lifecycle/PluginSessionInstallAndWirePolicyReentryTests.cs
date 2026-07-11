using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginSessionInstallAndWirePolicyReentryTests
{
    [Fact]
    public async Task Install_and_wire_policy_reentry_completes_instead_of_deadlocking()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        try
        {
            var session = server.CreateSession();

            var install = Task.Run(async () => await session.InstallAndWireAsync(
                FireDamagePluginPackage.Create(),
                _ => { },
                _package =>
                {
                    var ownsKernel = session.Owns("fire-damage");
                    Assert.False(ownsKernel);
                    return LongWallPluginPolicy();
                }).AsTask());

            var completed = await Task.WhenAny(install, Task.Delay(TimeSpan.FromSeconds(2))) == install;

            Assert.True(
                completed,
                "InstallAndWireAsync did not complete when its policy callback re-entered session ownership state.");

            var exception = await Record.ExceptionAsync(async () => Assert.NotNull(await install));
            Assert.True(
                exception is null or InvalidOperationException,
                "Unexpected install completion exception: " + exception);
        }
        finally
        {
            server.Dispose();
        }
    }

    private static SandboxPolicy LongWallPluginPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
