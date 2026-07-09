using System.Reflection;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Server.Reentrancy;

public sealed class DirectPipelineReentrantDisposalTests
{
    [Fact]
    public async Task Hook_pipeline_Use_rejects_reentrant_dispose_during_adapter_metadata_read()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new DisposingParametersDamageAdapter(server);
        var pipeline = server.Hooks.On<DamageEvent>(adapter);

        var exception = Record.Exception(() => pipeline.Use(kernel));
        var handlerCount = HandlerCount(pipeline);

        Assert.True(
            exception is ObjectDisposedException,
            "Expected ObjectDisposedException after adapter metadata disposed the server, " +
            $"but got {exception?.GetType().Name ?? "no exception"}; handler count: {handlerCount}.");
        Assert.Equal(0, handlerCount);
    }

    [Fact]
    public async Task Subscription_pipeline_Use_rejects_reentrant_dispose_during_adapter_metadata_read()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new DisposingParametersDamageAdapter(server);
        var pipeline = server.Subscriptions.On<DamageEvent>(adapter);

        var exception = Record.Exception(() => pipeline.Use(kernel));
        var handlerCount = HandlerCount(pipeline);

        Assert.True(
            exception is ObjectDisposedException,
            "Expected ObjectDisposedException after adapter metadata disposed the server, " +
            $"but got {exception?.GetType().Name ?? "no exception"}; handler count: {handlerCount}.");
        Assert.Equal(0, handlerCount);
    }

    private static int HandlerCount(object pipeline)
    {
        var handlerSetField = pipeline.GetType().GetField(
            "_handlerSet",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handlerSetField);

        var handlerSet = handlerSetField.GetValue(pipeline);
        Assert.NotNull(handlerSet);

        var snapshotProperty = handlerSet.GetType().GetProperty(
            "Snapshot",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(snapshotProperty);

        var snapshot = Assert.IsAssignableFrom<Array>(snapshotProperty.GetValue(handlerSet));
        return snapshot.Length;
    }

    private sealed class DisposingParametersDamageAdapter(PluginServer server) : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => nameof(DamageEvent);

        public IReadOnlyList<Parameter> Parameters
        {
            get
            {
                server.Dispose();
                return DamageEventAdapter.Instance.Parameters;
            }
        }

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => DamageEventAdapter.Instance.ToSandboxValues(e);
    }
}
