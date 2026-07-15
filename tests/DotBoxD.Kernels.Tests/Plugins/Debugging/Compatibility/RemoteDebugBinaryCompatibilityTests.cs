using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Compatibility;

public sealed class RemoteDebugBinaryCompatibilityTests
{
    [Fact]
    public void Existing_public_factory_and_constructor_signatures_remain_available()
    {
        Assert.NotNull(typeof(PluginServer).GetMethod(
            nameof(PluginServer.Create),
            [
                typeof(IPluginMessageSink),
                typeof(Action<SandboxHostBuilder>),
                typeof(SandboxPolicy),
                typeof(ExecutionMode),
                typeof(Action<SubscriptionDeliveryFault>),
                typeof(Action<ResultHookFault>)
            ]));
        Assert.NotNull(typeof(PluginPackage).GetMethod(
            nameof(PluginPackage.Create),
            [typeof(PluginManifest), typeof(SandboxModule), typeof(KernelEntrypoints)]));
        Assert.NotNull(typeof(SourceSpan).GetConstructor([typeof(int), typeof(int)]));
        Assert.NotNull(typeof(NamedPipeTransportOptions).GetConstructor([typeof(bool)]));
    }
}
