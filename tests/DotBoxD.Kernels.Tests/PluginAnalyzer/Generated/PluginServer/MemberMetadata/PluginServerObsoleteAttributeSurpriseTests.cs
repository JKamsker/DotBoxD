namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerObsoleteAttributeSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_preserves_obsolete_attributes_on_forwarded_members()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            #nullable enable
            using System;
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
                    [Obsolete("Use PingAsync")]
                    ValueTask<int> LegacyPingAsync();

                    [Obsolete("Use Creatures")]
                    IMonsterControl Monsters { get; }
                }

                [RpcService]
                public interface IMonsterControl
                {
                    [Obsolete("Use Rename")]
                    string RenameLegacy(string value);

                    [Obsolete("Use InventoryV2")]
                    IInventory Inventory { get; }
                }

                [RpcService]
                public interface IInventory
                {
                    [Obsolete("Use CountItems")]
                    int CountLegacy();
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
            """);

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        var normalized = NormalizeLineEndings(generated);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use PingAsync\")]\n" +
            "    public global::System.Threading.Tasks.ValueTask<int> LegacyPingAsync(",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use Creatures\")]\n" +
            "    public global::Regression.Game.IMonsterControl Monsters",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use Rename\")]\n" +
            "        public string RenameLegacy(",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use InventoryV2\")]\n" +
            "        public global::Regression.Game.IInventory Inventory",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Use CountItems\")]\n" +
            "            public int CountLegacy(",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_rejects_error_obsolete_forwarded_members()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            #nullable enable
            using System;
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
                    [Obsolete("Use ActivePingAsync", error: true)]
                    ValueTask<int> RemovedPingAsync();
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
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("RemovedPingAsync", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("error: true", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_plugin_server_rejects_error_obsolete_forwarded_property_accessors()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            #nullable enable
            using System;
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
                    IMonsterControl Monsters
                    {
                        [Obsolete("Use ActiveMonsters", error: true)]
                        get;
                    }
                }

                [RpcService]
                public interface IMonsterControl
                {
                    ValueTask<int> CountAsync();
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
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("Monsters", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("error: true", StringComparison.Ordinal));
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
