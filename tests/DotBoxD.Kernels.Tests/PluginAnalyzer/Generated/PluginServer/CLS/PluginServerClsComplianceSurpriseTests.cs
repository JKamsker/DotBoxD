namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerClsComplianceSurpriseTests
{
    [Fact]
    public void Generated_plugin_server_facade_does_not_emit_cls_warnings()
    {
        var (_, outputCompilation, generatorDiagnostics) = PluginServerGenerationTestDriver.RunWithDiagnostics("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            [assembly: CLSCompliant(true)]

            namespace Cls.Game
            {
                [RpcService]
                public interface IGameWorldAccess
                {
                    ValueTask<int> RollAsync(int sides, CancellationToken ct = default);
                }
            }

            namespace Cls.Game.Ipc
            {
                [CLSCompliant(false)]
                public readonly record struct LiveSettingUpdate(string Name, string Value);

                [CLSCompliant(false)]
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

                [CLSCompliant(false)]
                [RpcService]
                public interface IPluginEventCallback
                {
                    ValueTask OnEventAsync(
                        string subscriptionId,
                        ReadOnlyMemory<byte> projectedValue,
                        CancellationToken ct = default);

                    ValueTask<byte[]> OnResultAsync(
                        string subscriptionId,
                        ReadOnlyMemory<byte> contextValue,
                        CancellationToken ct = default);
                }
            }

            namespace DotBoxD.Services.Generated
            {
                [CLSCompliant(false)]
                public static class DotBoxDGeneratedExtensions
                {
                    public static Cls.Game.IGameWorldAccess GetGameWorldAccess(
                        DotBoxD.Services.Peer.RpcPeer peer)
                        => throw new InvalidOperationException("not used");

                    public static DotBoxD.Services.Peer.RpcPeer ProvidePluginEventCallback(
                        DotBoxD.Services.Peer.RpcPeer peer,
                        Cls.Game.Ipc.IPluginEventCallback implementation)
                        => peer;
                }
            }

            namespace Cls.Plugin
            {
                using DotBoxD.Abstractions;
                using Cls.Game;

                [GeneratePluginServer(Context = typeof(RemotePluginContext))]
                public partial class RemotePluginServer : IGameWorldAccess;

                public sealed partial class RemotePluginContext;
            }
            """);

        Assert.Empty(generatorDiagnostics);

        var clsDiagnostics = outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id is "CS3001" or "CS3002" or "CS3003")
            .ToArray();

        Assert.Empty(clsDiagnostics);
    }
}
