namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerPlatformCompatibilityAttributeSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_preserves_platform_compatibility_attributes_on_forwarded_members()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var normalized = generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> WindowsPingAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]\n" +
            "        public global::System.Threading.Tasks.ValueTask<int> WindowsRenameAsync(",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_keeps_unannotated_forwarded_members_clean()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var normalized = generated.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SupportedOSPlatformAttribute(\"windows\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> CrossPlatformPingAsync(",
            normalized,
            StringComparison.Ordinal);
    }

    private const string ServerSource = """
        #nullable enable
        using System.Runtime.Versioning;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Regression.Game
        {
            [RpcService]
            public interface IGameWorldAccess
            {
                [SupportedOSPlatform("windows")]
                ValueTask<int> WindowsPingAsync();

                ValueTask<int> CrossPlatformPingAsync();

                IMonsterControl Monsters { get; }
            }

            [RpcService]
            public interface IMonsterControl
            {
                [SupportedOSPlatform("windows")]
                ValueTask<int> WindowsRenameAsync(string value);
            }
        }

        namespace Regression.Game.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(
                    string pluginId,
                    LiveSettingUpdate[] updates,
                    bool atomic = false,
                    CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Regression.Game.IGameWorldAccess GetGameWorldAccess(
                    DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new System.InvalidOperationException("not used");
            }
        }

        namespace Regression.Plugin
        {
            using DotBoxD.Abstractions;
            using Regression.Game;

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;
        }
        """;
}
