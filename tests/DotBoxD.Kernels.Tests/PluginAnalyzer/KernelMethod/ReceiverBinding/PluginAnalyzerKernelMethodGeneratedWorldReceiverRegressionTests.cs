using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed class PluginAnalyzerKernelMethodGeneratedWorldReceiverRegressionTests
{
    [Fact]
    public void Context_KernelMethod_parameter_named_World_does_not_lower_string_receiver_as_world_binding()
    {
        const string source = """
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
            """;
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var descriptorSource = string.Join(
            "\n",
            result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Where(source => source.Contains(
                    "GeneratedKernelMethodDescriptorAttribute",
                    StringComparison.Ordinal)));
        const string expectedReceiverDiagnostic =
            "Unsupported plugin invocation 'World.Contains(value)'.";
        var hasReceiverDiagnostic = result.Diagnostics.Any(diagnostic =>
            diagnostic.Id == "DBXK100" &&
            string.Equals(
                diagnostic.GetMessage(),
                expectedReceiverDiagnostic,
                StringComparison.Ordinal));

        Assert.True(
            hasReceiverDiagnostic ||
            descriptorSource.Contains("GeneratedKernelMethodDescriptorAttribute", StringComparison.Ordinal),
            "Expected either a receiver-specific DBXK100 diagnostic or a generated descriptor.");
        Assert.DoesNotContain("host.Probe.IGameWorld.Contains", descriptorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("probe.read.contains", descriptorSource, StringComparison.Ordinal);
    }
}
