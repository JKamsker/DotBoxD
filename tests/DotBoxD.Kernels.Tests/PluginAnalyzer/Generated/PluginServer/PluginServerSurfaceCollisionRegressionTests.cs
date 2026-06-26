using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

// Regression coverage for generated-facade member collisions that previously surfaced as raw Roslyn
// CS errors (CS0111 / CS0102) instead of the designed DBXK100 "collides with the generated facade surface".
public sealed class PluginServerSurfaceCollisionRegressionTests
{
    [Fact]
    public void World_member_named_like_a_generated_private_helper_reports_dbxk100()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace ReservedHelper.Game
            {
                [DotBoxDService]
                public interface IGameWorldAccess
                {
                    void RequireWorld();
                    void Initialize();
                }
            }

            namespace ReservedHelper.Game.Ipc
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
                    public static ReservedHelper.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace ReservedHelper.Plugin
            {
                using DotBoxD.Abstractions;
                using ReservedHelper.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains("collides with the generated facade surface", StringComparison.Ordinal));
    }

    [Fact]
    public void World_member_appearing_in_two_facade_categories_reports_dbxk100()
    {
        var diagnostics = PluginServerGenerationTestDriver.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace CrossCategory.Game
            {
                [DotBoxDService]
                public interface IMonsterControl;

                [DotBoxDService]
                public interface ILeftWorld
                {
                    int Value { get; }
                }

                [DotBoxDService]
                public interface IRightWorld
                {
                    IMonsterControl Value { get; }
                }

                [DotBoxDService]
                public interface IGameWorldAccess : ILeftWorld, IRightWorld;
            }

            namespace CrossCategory.Game.Ipc
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
                    public static CrossCategory.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new System.InvalidOperationException("not used");
                }
            }

            namespace CrossCategory.Plugin
            {
                using DotBoxD.Abstractions;
                using CrossCategory.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains("more than one facade category", StringComparison.Ordinal));
    }
}
