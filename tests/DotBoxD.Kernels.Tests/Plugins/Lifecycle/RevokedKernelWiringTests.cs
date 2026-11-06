using System.Reflection;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using static DotBoxD.Kernels.Tests.Plugins.Hooks.Performance.ResultHookFanoutTestSupport;

namespace DotBoxD.Kernels.Tests.Plugins.Lifecycle;

// Revocation detaches a kernel's handlers once; a handler added after that sweep would stay forever. These pin that
// wiring a kernel revoked before (or while) it is wired never leaves a dead handler behind.
public sealed class RevokedKernelWiringTests
{
    [Fact]
    public async Task Hook_Use_does_not_keep_handler_for_kernel_revoked_while_wiring()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new UninstallingParametersDamageAdapter(server, kernel.Manifest.PluginId);
        var pipeline = server.Hooks.On<DamageEvent>(adapter);

        adapter.Armed = true;
        pipeline.Use(kernel);

        Assert.True(kernel.IsRevoked);
        Assert.Equal(0, HandlerCount(pipeline));
    }

    [Fact]
    public async Task Subscription_Use_does_not_keep_handler_for_kernel_revoked_while_wiring()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new UninstallingParametersDamageAdapter(server, kernel.Manifest.PluginId);
        var pipeline = server.Subscriptions.On<DamageEvent>(adapter);

        adapter.Armed = true;
        pipeline.Use(kernel);

        Assert.True(kernel.IsRevoked);
        Assert.Equal(0, HandlerCount(pipeline));
    }

    [Fact]
    public async Task Hook_Use_does_not_add_handler_for_already_revoked_kernel()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        Assert.True(server.Uninstall(kernel.Manifest.PluginId));

        var pipeline = server.Hooks.On<DamageEvent>(DamageEventAdapter.Instance).Use(kernel);

        Assert.Equal(0, HandlerCount(pipeline));
    }

    [Fact]
    public async Task Result_hook_slot_does_not_add_entry_for_revoked_kernel()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        Assert.True(server.Uninstall(kernel.Manifest.PluginId));
        var pipeline = server.Hooks.On<ProbeEvent>(Adapter);

        AddSandboxHandler(pipeline, kernel, priority: 0);

        Assert.Empty(pipeline.ResultRegistrations().Registrations);
    }

    private static int HandlerCount(object pipeline)
    {
        var handlerSet = pipeline.GetType()
            .GetField("_handlerSet", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(pipeline)!;
        var snapshot = handlerSet.GetType()
            .GetProperty("Snapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(handlerSet);
        return Assert.IsAssignableFrom<Array>(snapshot).Length;
    }

    // Uninstalls (and so revokes) the kernel from inside its own wiring: ValidateFor reads Parameters before the
    // handler is added, which is after Revoke has already swept the pipelines.
    private sealed class UninstallingParametersDamageAdapter(PluginServer server, string pluginId)
        : IPluginEventAdapter<DamageEvent>
    {
        public bool Armed { get; set; }

        public string EventName => nameof(DamageEvent);

        public IReadOnlyList<Parameter> Parameters
        {
            get
            {
                if (Armed)
                {
                    Armed = false;
                    server.Uninstall(pluginId);
                }

                return DamageEventAdapter.Instance.Parameters;
            }
        }

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => DamageEventAdapter.Instance.ToSandboxValues(e);
    }
}
