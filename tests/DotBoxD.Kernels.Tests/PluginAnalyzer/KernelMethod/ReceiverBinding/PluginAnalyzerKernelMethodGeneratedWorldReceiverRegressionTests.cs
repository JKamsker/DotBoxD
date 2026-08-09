using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed class PluginAnalyzerKernelMethodGeneratedWorldReceiverRegressionTests
{
    [Fact]
    public void Context_KernelMethod_parameter_named_World_does_not_lower_string_receiver_as_world_binding()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Services.Attributes;

            namespace Probe
            {
                [RpcService]
                public interface IGameWorld
                {
                    [HostBinding("host.Probe.IGameWorld.Contains", "probe.read.contains", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    bool Contains(string value);
                }

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public partial class GamePluginServer : IGameWorld;

                public sealed partial class GamePluginContext
                {
                    [KernelMethod]
                    public bool IsAllowed(string World, string value) => World.Contains(value);
                }
            }

            namespace Probe.Ipc
            {
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
                {
                    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                    ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default);
                    ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
                }
            }
            """);
        var descriptorSource = string.Join(
            "\n",
            result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Where(source => source.Contains(
                    "GeneratedKernelMethodDescriptorAttribute",
                    StringComparison.Ordinal)));

        Assert.True(
            result.Diagnostics.Any(diagnostic => diagnostic.Id == "DBXK100") ||
            descriptorSource.Contains("GeneratedKernelMethodDescriptorAttribute", StringComparison.Ordinal),
            "Expected either a focused DBXK100 unsupported-shape diagnostic or a generated descriptor.");
        Assert.DoesNotContain("host.Probe.IGameWorld.Contains", descriptorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("probe.read.contains", descriptorSource, StringComparison.Ordinal);
    }
}
