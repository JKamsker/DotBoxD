using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGeneratedBuilderResolutionSurpriseTests
{
    [Fact]
    public void Generated_builder_local_uses_same_named_facade_from_its_namespace()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Kernels.Game.Server.Abstractions;
            using DotBoxD.Kernels.Game.Server.Abstractions.Ipc;

            namespace DotBoxD.Kernels.Game.Server.Abstractions
            {
                [RpcService]
                public interface IGameWorldAccess
                {
                    [HostBinding("host.world.getHealth", "game.world.monster.read.health", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    int GetHealth(string entityId);
                }

                [RpcService]
                public interface IAlternateWorld
                {
                    [HostBinding("host.world.getScore", "game.world.score.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    int GetScore(string entityId);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                public static class DotBoxDGeneratedExtensions
                {
                    public static IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");

                    public static IAlternateWorld GetAlternateWorld(DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");
                }
            }

            namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
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

            namespace DotBoxD.Kernels.Game.Plugin.Client
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }

            namespace Other
            {
                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IAlternateWorld;

                public sealed partial class RemotePluginContext;
            }

            namespace Sample
            {
                public static class Usage
                {
                    public static ValueTask<int> Run(IGamePluginControlService control)
                    {
                        var server = Other.RemotePluginServerBuilder.FromConnection(control).Build();
                        return server.InvokeAsync(async (IAlternateWorld world) =>
                        {
                            return world.GetScore("quest-1");
                        });
                    }
                }
            }
            """);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
        Assert.Contains("IAlternateWorld", source, StringComparison.Ordinal);
    }
}
