namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerParameterFlowAttributeSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_preserves_parameter_flow_attributes_on_forwarded_methods()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
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
                    string? Inspect(
                        [AllowNull] string key,
                        [MaybeNullWhen(false)] string fallback,
                        [NotNullIfNotNull(nameof(key))] string? echo);

                    IMonsterControl Monsters { get; }
                }

                [RpcService]
                public interface IMonsterControl
                {
                    bool TryRename(
                        [DisallowNull] string? name,
                        [NotNullWhen(true)] string? normalized,
                        [MaybeNull] string reason);
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
        Assert.Contains(
            "Inspect([global::System.Diagnostics.CodeAnalysis.AllowNullAttribute] string @key, " +
            "[global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute(false)] string @fallback, " +
            "[global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute(\"key\")] string? @echo)",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "TryRename([global::System.Diagnostics.CodeAnalysis.DisallowNullAttribute] string? @name, " +
            "[global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] string? @normalized, " +
            "[global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute] string @reason)",
            generated,
            StringComparison.Ordinal);
    }
}
